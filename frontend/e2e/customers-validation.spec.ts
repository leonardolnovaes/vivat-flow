import { expect, test } from './customers-runtime.fixture'
import { api, cnpj, contactInput, createCustomer, customerInput, freshSeed, getCustomer, readyAdmin, unitInput } from './customers-hardening.helpers'

test('Customer fields validate boundaries, normalize text and reject duplicate inactive CNPJ', async ({ page }) => {
  await readyAdmin(page)
  const seed = freshSeed()
  const invalid = [
    [{ legalName: null }, 'legalName'], [{ legalName: '   ' }, 'legalName'], [{ legalName: 'A' }, 'legalName'],
    [{ legalName: 'A'.repeat(201) }, 'legalName'], [{ tradeName: 'A' }, 'tradeName'],
    [{ tradeName: 'T'.repeat(201) }, 'tradeName'], [{ notes: 'N'.repeat(2001) }, 'notes'],
    [{ cnpj: '123' }, 'cnpj'], [{ cnpj: '1'.repeat(15) }, 'cnpj'],
    [{ cnpj: cnpj(seed).slice(0, 13) + (cnpj(seed)[13] === '0' ? '1' : '0') }, 'cnpj'],
    [{ cnpj: 'ABCDEFGHIJKLMN' }, 'cnpj']
  ] as const
  for (const [changes, field] of invalid) {
    const result = await api<{ errors: Record<string, string[]> }>(page, 'POST', '/api/customers', customerInput(seed, changes))
    expect(result.status, JSON.stringify(changes)).toBe(400)
    expect(result.body.errors[field]?.[0]).toBeTruthy()
    expect(JSON.stringify(result.body)).not.toMatch(/Npgsql|PostgresException|DbUpdateException/i)
  }
  const accepted = await createCustomer(page, seed, {
    legalName: '  Açúcar Empresa  ', tradeName: '  Comércio Épsilon  ', notes: '  Linha um\nLinha dois 😀  ',
    cnpj: cnpj(seed).replace(/(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})/, '$1.$2.$3/$4-$5')
  })
  expect(accepted).toMatchObject({ legalName: 'Açúcar Empresa', tradeName: 'Comércio Épsilon', notes: 'Linha um\nLinha dois 😀', cnpj: cnpj(seed) })
  const inactive = await api(page, 'POST', `/api/customers/${accepted.id}/deactivate`, { expectedVersion: accepted.version })
  expect(inactive.status).toBe(200)
  const duplicate = await api<{ errors: Record<string, string[]> }>(page, 'POST', '/api/customers', customerInput(seed))
  expect(duplicate.status).toBe(409)
  expect(duplicate.body.errors.cnpj[0]).toMatch(/CNPJ/)
  const list = await api<{ totalCount: number }>(page, 'GET', `/api/customers?search=${cnpj(seed)}`)
  expect(list.body.totalCount).toBe(1)
  const minimum = await createCustomer(page, freshSeed(), { legalName: 'AB', tradeName: null, notes: '   ' })
  expect(minimum.notes).toBeNull()
  const maximum = await createCustomer(page, freshSeed(), { legalName: 'L'.repeat(200), tradeName: 'T'.repeat(200), notes: 'N'.repeat(2000) })
  expect(maximum.legalName).toHaveLength(200)
  expect(maximum.tradeName).toHaveLength(200)
  expect(maximum.notes).toHaveLength(2000)
})

test('Contact channels, phone normalization and validation preserve aggregate version', async ({ page }) => {
  await readyAdmin(page)
  const customer = await createCustomer(page, freshSeed())
  const path = `/api/customers/${customer.id}/contacts`
  const invalid = [
    [{ name: 'A' }, 'name'], [{ name: 'N'.repeat(121) }, 'name'],
    [{ email: null, phone: null }, 'contact'], [{ email: ' ', phone: ' ' }, 'contact'],
    [{ email: 'broken' }, 'email'], [{ phone: '123', email: null }, 'phone'],
    [{ phone: '1'.repeat(14), email: null }, 'phone'], [{ phone: '11 99999-9999 ext 2', email: null }, 'phone'],
    [{ roleOrDepartment: 'R'.repeat(121) }, 'roleOrDepartment']
  ] as const
  for (const [changes, field] of invalid) {
    const result = await api<{ errors: Record<string, string[]> }>(page, 'POST', path, { ...contactInput(changes), expectedVersion: customer.version })
    expect(result.status, JSON.stringify(changes)).toBe(400)
    expect(result.body.errors[field]?.[0]).toBeTruthy()
  }
  expect((await getCustomer(page, customer.id)).version).toBe(customer.version)
  const first = await api<{ contact: { id: string; email: string; phone: null }; version: string }>(page, 'POST', path,
    { ...contactInput({ name: 'Álvaro', email: ' MIXED@EXAMPLE.TEST ', phone: '' }), expectedVersion: customer.version })
  expect(first.status).toBe(201)
  expect(first.body.contact).toMatchObject({ email: 'mixed@example.test', phone: null })
  const phoneCases = [
    ['(11) 3333-4444', '1133334444'], ['(11) 99999-8888', '11999998888'],
    ['+55 (11) 99999-7777', '11999997777'], ['5511999996666', '11999996666']
  ] as const
  let version = first.body.version
  for (const [phone, expected] of phoneCases) {
    const result = await api<{ contact: { phone: string }; version: string }>(page, 'POST', path,
      { ...contactInput({ email: null, phone }), expectedVersion: version })
    expect(result.status, phone).toBe(201)
    expect(result.body.contact.phone).toBe(expected)
    version = result.body.version
  }
  expect((await getCustomer(page, customer.id)).contacts).toHaveLength(5)
  const boundaryCases = [
    { name: 'AB', roleOrDepartment: 'R'.repeat(120), email: '  LOWER@EXAMPLE.TEST  ', phone: '   ' },
    { name: 'N'.repeat(120), roleOrDepartment: '   ', email: null, phone: '1133334444' }
  ]
  for (const input of boundaryCases) {
    const result = await api<{ contact: { name: string; roleOrDepartment: string | null; email: string | null; phone: string | null }; version: string }>(page, 'POST', path,
      { ...contactInput(input), expectedVersion: version })
    expect(result.status).toBe(201)
    expect(result.body.contact.name).toBe(input.name)
    expect(result.body.contact.roleOrDepartment).toBe(input.roleOrDepartment.trim() || null)
    if (input.email) expect(result.body.contact.email).toBe('lower@example.test')
    version = result.body.version
  }
})

test('Unit required fields, UF and CEP validation preserve data; valid optional fields normalize', async ({ page }) => {
  await readyAdmin(page)
  const customer = await createCustomer(page, freshSeed())
  const path = `/api/customers/${customer.id}/units`
  const invalid = [
    [{ name: 'A' }, 'name'], [{ name: 'N'.repeat(161) }, 'name'],
    [{ street: null }, 'street'], [{ number: ' ' }, 'number'], [{ city: null }, 'city'],
    [{ stateCode: null }, 'stateCode'], [{ stateCode: 'XX' }, 'stateCode'],
    [{ street: 'S'.repeat(161) }, 'street'], [{ number: '1'.repeat(31) }, 'number'],
    [{ complement: 'C'.repeat(121) }, 'complement'], [{ district: 'D'.repeat(121) }, 'district'],
    [{ city: 'C'.repeat(121) }, 'city'],
    [{ postalCode: '123' }, 'postalCode'], [{ postalCode: '123456789' }, 'postalCode'],
    [{ postalCode: 'ABCDEFGH' }, 'postalCode']
  ] as const
  for (const [changes, field] of invalid) {
    const result = await api<{ errors: Record<string, string[]> }>(page, 'POST', path, { ...unitInput(changes), expectedVersion: customer.version })
    expect(result.status, JSON.stringify(changes)).toBe(400)
    expect(result.body.errors[field]?.[0]).toBeTruthy()
  }
  expect((await getCustomer(page, customer.id)).units).toHaveLength(0)
  const accepted = await api<{ unit: { postalCode: string; name: string }; version: string }>(page, 'POST', path,
    { ...unitInput({ name: 'Matriz Açúcar', street: 'Rua São João', city: 'Belo Horizonte', stateCode: 'mg', postalCode: '30100-000' }), expectedVersion: customer.version })
  expect(accepted.status).toBe(201)
  expect(accepted.body.unit).toMatchObject({ name: 'Matriz Açúcar', postalCode: '30100000' })
  const optional = await api<{ unit: { postalCode: null; complement: null; district: null }; version: string }>(page, 'POST', path,
    { ...unitInput({ complement: '', district: '', postalCode: '' }), expectedVersion: accepted.body.version })
  expect(optional.status).toBe(201)
  expect(optional.body.unit.postalCode).toBeNull()
  expect(optional.body.unit.complement).toBeNull()
  expect(optional.body.unit.district).toBeNull()
  let version = optional.body.version
  for (const changes of [
    { name: 'AB', street: 'S'.repeat(160), number: '1'.repeat(30), complement: 'C'.repeat(120), district: 'D'.repeat(120), city: 'C'.repeat(120), postalCode: '01001000' },
    { name: 'N'.repeat(160), street: 'S', number: '1', city: 'C', postalCode: null }
  ]) {
    const result = await api<{ unit: { name: string; postalCode: string | null }; version: string }>(page, 'POST', path,
      { ...unitInput(changes), expectedVersion: version })
    expect(result.status).toBe(201)
    expect(result.body.unit.name).toBe(changes.name)
    expect(result.body.unit.postalCode).toBe(changes.postalCode)
    version = result.body.version
  }
})

test('Primary Contact transition leaves no automatic replacement and blocks conflicting reactivation', async ({ page }) => {
  await readyAdmin(page)
  const customer = await createCustomer(page, freshSeed())
  const path = `/api/customers/${customer.id}/contacts`
  const primaryInput = contactInput({ name: 'Primary Contact', isPrimary: true })
  const first = await api<{ contact: { id: string }; version: string }>(page, 'POST', path, { ...primaryInput, expectedVersion: customer.version })
  expect(first.status).toBe(201)
  const secondInput = contactInput({ name: 'Replacement Contact' })
  const second = await api<{ contact: { id: string }; version: string }>(page, 'POST', path, { ...secondInput, expectedVersion: first.body.version })
  expect(second.status).toBe(201)
  const deactivated = await api<{ version: string }>(page, 'POST', `${path}/${first.body.contact.id}/deactivate`, { expectedVersion: second.body.version })
  expect(deactivated.status).toBe(200)
  let stored = await getCustomer(page, customer.id)
  expect(stored.contacts.find(item => item.id === first.body.contact.id)).toMatchObject({ isActive: false, isPrimary: true })
  expect(stored.contacts.find(item => item.id === second.body.contact.id)?.isPrimary).toBe(false)
  const promoted = await api<{ version: string }>(page, 'PUT', `${path}/${second.body.contact.id}`,
    { ...secondInput, isPrimary: true, expectedVersion: deactivated.body.version })
  expect(promoted.status).toBe(200)
  expect((await api(page, 'POST', `${path}/${first.body.contact.id}/activate`, { expectedVersion: promoted.body.version })).status).toBe(409)
  stored = await getCustomer(page, customer.id)
  expect(stored.contacts.find(item => item.id === first.body.contact.id)?.isActive).toBe(false)
  expect(stored.contacts.filter(item => item.isActive && item.isPrimary)).toHaveLength(1)
  const demoted = await api<{ version: string }>(page, 'PUT', `${path}/${first.body.contact.id}`,
    { ...primaryInput, isPrimary: false, expectedVersion: stored.version })
  expect(demoted.status).toBe(200)
  expect((await api(page, 'POST', `${path}/${first.body.contact.id}/activate`, { expectedVersion: demoted.body.version })).status).toBe(200)
  stored = await getCustomer(page, customer.id)
  expect(stored.contacts.find(item => item.id === second.body.contact.id)).toMatchObject({ isActive: true, isPrimary: true })
  expect(stored.contacts.filter(item => item.isActive && item.isPrimary)).toHaveLength(1)
})

test('Primary Unit transition leaves no automatic replacement and blocks conflicting reactivation', async ({ page }) => {
  await readyAdmin(page)
  const customer = await createCustomer(page, freshSeed())
  const path = `/api/customers/${customer.id}/units`
  const primaryInput = unitInput({ name: 'Primary Unit', isPrimary: true })
  const first = await api<{ unit: { id: string }; version: string }>(page, 'POST', path, { ...primaryInput, expectedVersion: customer.version })
  expect(first.status).toBe(201)
  const secondInput = unitInput({ name: 'Replacement Unit' })
  const second = await api<{ unit: { id: string }; version: string }>(page, 'POST', path, { ...secondInput, expectedVersion: first.body.version })
  expect(second.status).toBe(201)
  const deactivated = await api<{ version: string }>(page, 'POST', `${path}/${first.body.unit.id}/deactivate`, { expectedVersion: second.body.version })
  expect(deactivated.status).toBe(200)
  let stored = await getCustomer(page, customer.id)
  expect(stored.units.find(item => item.id === first.body.unit.id)).toMatchObject({ isActive: false, isPrimary: true })
  expect(stored.units.find(item => item.id === second.body.unit.id)?.isPrimary).toBe(false)
  const promoted = await api<{ version: string }>(page, 'PUT', `${path}/${second.body.unit.id}`,
    { ...secondInput, isPrimary: true, expectedVersion: deactivated.body.version })
  expect(promoted.status).toBe(200)
  expect((await api(page, 'POST', `${path}/${first.body.unit.id}/activate`, { expectedVersion: promoted.body.version })).status).toBe(409)
  stored = await getCustomer(page, customer.id)
  expect(stored.units.find(item => item.id === first.body.unit.id)?.isActive).toBe(false)
  expect(stored.units.filter(item => item.isActive && item.isPrimary)).toHaveLength(1)
  const demoted = await api<{ version: string }>(page, 'PUT', `${path}/${first.body.unit.id}`,
    { ...primaryInput, isPrimary: false, expectedVersion: stored.version })
  expect(demoted.status).toBe(200)
  expect((await api(page, 'POST', `${path}/${first.body.unit.id}/activate`, { expectedVersion: demoted.body.version })).status).toBe(200)
  stored = await getCustomer(page, customer.id)
  expect(stored.units.find(item => item.id === second.body.unit.id)).toMatchObject({ isActive: true, isPrimary: true })
  expect(stored.units.filter(item => item.isActive && item.isPrimary)).toHaveLength(1)
})
