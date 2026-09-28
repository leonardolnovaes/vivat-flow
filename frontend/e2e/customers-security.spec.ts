import { expect, test } from './customers-runtime.fixture'
import { api, contactInput, createCustomer, createRoleSession, customerInput, freshSeed, getCustomer, readyAdmin, unitInput, unique } from './customers-hardening.helpers'

test('Customer, Contact and Unit reject missing or invalid CSRF and recover through valid requests', async ({ page }) => {
  await readyAdmin(page)
  const input = customerInput(freshSeed())
  for (const csrf of ['missing', 'invalid'] as const) {
    expect((await api(page, 'POST', '/api/customers', input, csrf)).status).toBe(400)
  }
  const customerResult = await api<{ id: string; version: string }>(page, 'POST', '/api/customers', input)
  expect(customerResult.status).toBe(201)
  const customer = customerResult.body
  const contact = { ...contactInput(), expectedVersion: customer.version }
  const unit = { ...unitInput(), expectedVersion: customer.version }
  for (const pathAndBody of [[`/api/customers/${customer.id}/contacts`, contact], [`/api/customers/${customer.id}/units`, unit]] as const) {
    for (const csrf of ['missing', 'invalid'] as const) {
      expect((await api(page, 'POST', pathAndBody[0], pathAndBody[1], csrf)).status).toBe(400)
    }
  }
  const contactResult = await api(page, 'POST', `/api/customers/${customer.id}/contacts`, contact)
  expect(contactResult.status).toBe(201)
  const afterContact = await getCustomer(page, customer.id)
  expect((await api(page, 'POST', `/api/customers/${customer.id}/units`, { ...unit, expectedVersion: afterContact.version })).status).toBe(201)
  const final = await getCustomer(page, customer.id)
  expect(final.contacts).toHaveLength(1)
  expect(final.units).toHaveLength(1)
})

test('Contact and Unit IDs cannot be mutated through another Customer', async ({ page }) => {
  await readyAdmin(page)
  const first = await createCustomer(page, freshSeed())
  const second = await createCustomer(page, freshSeed())
  const contact = await api<{ contact: { id: string }; version: string }>(page, 'POST', `/api/customers/${first.id}/contacts`,
    { ...contactInput(), expectedVersion: first.version })
  expect(contact.status).toBe(201)
  const unit = await api<{ unit: { id: string }; version: string }>(page, 'POST', `/api/customers/${first.id}/units`,
    { ...unitInput(), expectedVersion: contact.body.version })
  expect(unit.status).toBe(201)
  const contactPath = `/api/customers/${second.id}/contacts/${contact.body.contact.id}`
  const unitPath = `/api/customers/${second.id}/units/${unit.body.unit.id}`
  const attempts = [
    ['PUT', contactPath, { ...contactInput({ name: 'Changed' }), expectedVersion: second.version }],
    ['POST', `${contactPath}/deactivate`, { expectedVersion: second.version }],
    ['PUT', unitPath, { ...unitInput({ name: 'Changed' }), expectedVersion: second.version }],
    ['POST', `${unitPath}/deactivate`, { expectedVersion: second.version }]
  ] as const
  for (const [method, path, body] of attempts) expect((await api(page, method, path, body)).status).toBe(404)
  expect((await getCustomer(page, second.id)).version).toBe(second.version)
  const unchanged = await getCustomer(page, first.id)
  expect(unchanged.contacts[0]).toMatchObject({ id: contact.body.contact.id, isActive: true })
  expect(unchanged.units[0]).toMatchObject({ id: unit.body.unit.id, isActive: true })
})

test('Server-owned Customer and child fields cannot be mass assigned', async ({ page }) => {
  await readyAdmin(page)
  const forgedId = crypto.randomUUID()
  const forgedUser = crypto.randomUUID()
  const oldDate = '2001-01-01T00:00:00Z'
  const customer = await createCustomer(page, freshSeed(), {
    id: forgedId, createdAtUtc: oldDate, updatedAtUtc: oldDate, createdByUserId: forgedUser,
    updatedByUserId: forgedUser, isActive: false, version: forgedId
  })
  expect(customer.id).not.toBe(forgedId)
  expect(customer.version).not.toBe(forgedId)
  expect(customer.createdByUserId).not.toBe(forgedUser)
  expect(customer.updatedByUserId).not.toBe(forgedUser)
  expect(customer.createdAtUtc).not.toBe(oldDate)
  expect(customer.isActive).toBe(true)
  const update = await api<{ version: string; isActive: boolean; updatedByUserId: string }>(page, 'PUT', `/api/customers/${customer.id}`,
    { ...customerInput(freshSeed(), { cnpj: customer.cnpj }), expectedVersion: customer.version, isActive: false, version: forgedId, updatedByUserId: forgedUser })
  expect(update.status).toBe(200)
  expect(update.body.isActive).toBe(true)
  expect(update.body.version).not.toBe(forgedId)
  expect(update.body.updatedByUserId).not.toBe(forgedUser)
  const contact = await api<{ contact: { id: string }; version: string }>(page, 'POST', `/api/customers/${customer.id}/contacts`,
    { ...contactInput(), customerId: forgedId, id: forgedId, isActive: false, expectedVersion: update.body.version })
  expect(contact.status).toBe(201)
  const unit = await api<{ unit: { id: string }; version: string }>(page, 'POST', `/api/customers/${customer.id}/units`,
    { ...unitInput(), customerId: forgedId, id: forgedId, isActive: false, expectedVersion: contact.body.version })
  expect(unit.status).toBe(201)
  const stored = await getCustomer(page, customer.id)
  expect(stored.contacts[0].id).not.toBe(forgedId)
  expect(stored.units[0].id).not.toBe(forgedId)
  expect(stored.contacts[0].isActive).toBe(true)
  expect(stored.units[0].isActive).toBe(true)
})

test('Unauthenticated Customer mutations cannot create data', async ({ browser }) => {
  const context = await browser.newContext({ storageState: { cookies: [], origins: [] } })
  const page = await context.newPage()
  try {
  await page.goto('/')
  const input = customerInput(freshSeed(), { legalName: `E2E Anonymous ${unique()}` })
  expect((await api(page, 'POST', '/api/customers', input)).status).toBe(401)
  } finally { await context.close() }
})

test('USER is denied direct mutations and inactive Customer visibility while MANAGER can mutate', async ({ newIsolatedPage }) => {
  const admin = await newIsolatedPage()
  const manager = await newIsolatedPage()
  const user = await newIsolatedPage()
  try {
    await readyAdmin(admin)
    await createRoleSession(admin, manager, 'MANAGER')
    await createRoleSession(admin, user, 'USER')
    const original = await createCustomer(admin, freshSeed())
    const contact = await api<{ contact: { id: string }; version: string }>(admin, 'POST', `/api/customers/${original.id}/contacts`,
      { ...contactInput(), expectedVersion: original.version })
    expect(contact.status).toBe(201)
    const unit = await api<{ unit: { id: string }; version: string }>(admin, 'POST', `/api/customers/${original.id}/units`,
      { ...unitInput(), expectedVersion: contact.body.version })
    expect(unit.status).toBe(201)
    const customer = await getCustomer(admin, original.id)
    const mutations = [
      ['POST', '/api/customers', customerInput(freshSeed())],
      ['PUT', `/api/customers/${customer.id}`, { ...customerInput(freshSeed(), { cnpj: customer.cnpj }), expectedVersion: customer.version }],
      ['POST', `/api/customers/${customer.id}/deactivate`, { expectedVersion: customer.version }],
      ['POST', `/api/customers/${customer.id}/activate`, { expectedVersion: customer.version }],
      ['POST', `/api/customers/${customer.id}/contacts`, { ...contactInput(), expectedVersion: customer.version }],
      ['PUT', `/api/customers/${customer.id}/contacts/${contact.body.contact.id}`, { ...contactInput(), expectedVersion: customer.version }],
      ['POST', `/api/customers/${customer.id}/contacts/${contact.body.contact.id}/deactivate`, { expectedVersion: customer.version }],
      ['POST', `/api/customers/${customer.id}/contacts/${contact.body.contact.id}/activate`, { expectedVersion: customer.version }],
      ['POST', `/api/customers/${customer.id}/units`, { ...unitInput(), expectedVersion: customer.version }],
      ['PUT', `/api/customers/${customer.id}/units/${unit.body.unit.id}`, { ...unitInput(), expectedVersion: customer.version }],
      ['POST', `/api/customers/${customer.id}/units/${unit.body.unit.id}/deactivate`, { expectedVersion: customer.version }],
      ['POST', `/api/customers/${customer.id}/units/${unit.body.unit.id}/activate`, { expectedVersion: customer.version }]
    ] as const
    for (const [method, path, body] of mutations) expect((await api(user, method, path, body)).status, `${method} ${path}`).toBe(403)
    expect((await getCustomer(admin, customer.id)).version).toBe(customer.version)
    const managerCreated = await createCustomer(manager, freshSeed())
    const managerContact = await api<{ version: string }>(manager, 'POST', `/api/customers/${managerCreated.id}/contacts`,
      { ...contactInput(), expectedVersion: managerCreated.version })
    expect(managerContact.status).toBe(201)
    const managerUnit = await api(manager, 'POST', `/api/customers/${managerCreated.id}/units`,
      { ...unitInput(), expectedVersion: managerContact.body.version })
    expect(managerUnit.status).toBe(201)
    const current = await getCustomer(manager, managerCreated.id)
    expect((await api(manager, 'POST', `/api/customers/${managerCreated.id}/deactivate`, { expectedVersion: current.version })).status).toBe(200)
    expect((await api(user, 'GET', `/api/customers/${managerCreated.id}`)).status).toBe(404)
    const manipulated = await api<{ items: { id: string }[] }>(user, 'GET', '/api/customers?isActive=false')
    expect(manipulated.body.items.some(item => item.id === managerCreated.id)).toBe(false)
    await user.goto(`/clientes/${managerCreated.id}`)
    await expect(user.getByRole('heading', { name: /não encontrado|indisponível/i })).toBeVisible()
    expect((await api(manager, 'GET', `/api/customers/${managerCreated.id}`)).status).toBe(200)
  } finally { await admin.close(); await manager.close(); await user.close() }
})

test('MANAGER completes every approved Customer, Contact and Unit mutation and cannot administer users', async ({ newIsolatedPage }) => {
  const admin = await newIsolatedPage()
  const manager = await newIsolatedPage()
  try {
    await readyAdmin(admin)
    await createRoleSession(admin, manager, 'MANAGER')
    await manager.goto('/admin/usuarios')
    await expect(manager.getByRole('heading', { name: 'Acesso negado' })).toBeVisible()
    expect((await api(manager, 'GET', '/api/admin/users')).status).toBe(403)

    const customer = await createCustomer(manager, freshSeed())
    const path = `/api/customers/${customer.id}`
    let version = customer.version
    const apply = async (method: string, endpoint: string, body: Record<string, unknown>, expectedStatus = 200) => {
      const result = await api<{ version: string }>(manager, method, endpoint, { ...body, expectedVersion: version })
      expect(result.status, `${method} ${endpoint}`).toBe(expectedStatus)
      version = result.body.version
    }
    await apply('PUT', path, { legalName: 'E2E Manager Updated', tradeName: null, cnpj: customer.cnpj, notes: null })
    const contactPath = `${path}/contacts`
    const firstContact = await api<{ contact: { id: string }; version: string }>(manager, 'POST', contactPath,
      { ...contactInput({ isPrimary: true }), expectedVersion: version })
    expect(firstContact.status).toBe(201)
    version = firstContact.body.version
    const secondContact = await api<{ contact: { id: string }; version: string }>(manager, 'POST', contactPath,
      { ...contactInput(), expectedVersion: version })
    expect(secondContact.status).toBe(201)
    version = secondContact.body.version
    await apply('PUT', `${contactPath}/${firstContact.body.contact.id}`, contactInput({ name: 'Manager Edited Contact', isPrimary: false }))
    await apply('PUT', `${contactPath}/${secondContact.body.contact.id}`, contactInput({ name: 'Manager Primary Contact', isPrimary: true }))
    await apply('POST', `${contactPath}/${firstContact.body.contact.id}/deactivate`, {})
    await apply('POST', `${contactPath}/${firstContact.body.contact.id}/activate`, {})

    const unitPath = `${path}/units`
    const firstUnit = await api<{ unit: { id: string }; version: string }>(manager, 'POST', unitPath,
      { ...unitInput({ isPrimary: true }), expectedVersion: version })
    expect(firstUnit.status).toBe(201)
    version = firstUnit.body.version
    const secondUnit = await api<{ unit: { id: string }; version: string }>(manager, 'POST', unitPath,
      { ...unitInput(), expectedVersion: version })
    expect(secondUnit.status).toBe(201)
    version = secondUnit.body.version
    await apply('PUT', `${unitPath}/${firstUnit.body.unit.id}`, unitInput({ name: 'Manager Edited Unit', isPrimary: false }))
    await apply('PUT', `${unitPath}/${secondUnit.body.unit.id}`, unitInput({ name: 'Manager Primary Unit', isPrimary: true }))
    await apply('POST', `${unitPath}/${firstUnit.body.unit.id}/deactivate`, {})
    await apply('POST', `${unitPath}/${firstUnit.body.unit.id}/activate`, {})
    await apply('POST', `${path}/deactivate`, {})
    await apply('POST', `${path}/activate`, {})
    const stored = await getCustomer(manager, customer.id)
    expect(stored).toMatchObject({ isActive: true, legalName: 'E2E Manager Updated' })
    expect(stored.contacts.filter(item => item.isActive && item.isPrimary)).toHaveLength(1)
    expect(stored.units.filter(item => item.isActive && item.isPrimary)).toHaveLength(1)
  } finally { await admin.close(); await manager.close() }
})
