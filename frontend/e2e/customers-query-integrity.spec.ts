import { expect, test } from './customers-runtime.fixture'
import { api, cnpj, contactInput, createCustomer, customerInput, freshSeed, getCustomer, readyAdmin, unique } from './customers-hardening.helpers'

test('Search, filters, sorting and pagination use stable bounded results', async ({ page }) => {
  await readyAdmin(page)
  const marker = `E2E Query ${unique()}`
  const created: { id: string; legalName: string; tradeName: string | null }[] = []
  for (let index = 0; index < 27; index++) {
    const customer = await createCustomer(page, freshSeed(), {
      legalName: `${marker} ${String(index).padStart(2, '0')}`,
      tradeName: `${marker} Trade ${String(26 - index).padStart(2, '0')}`
    })
    created.push(customer)
  }
  const first = created[0]
  const contactEmail = `query-${unique()}@example.test`
  const contact = await api(page, 'POST', `/api/customers/${first.id}/contacts`, {
    ...contactInput({ name: `${marker} Contact`, email: contactEmail }),
    expectedVersion: (await getCustomer(page, first.id)).version
  })
  expect(contact.status).toBe(201)
  const updated = await getCustomer(page, first.id)
  expect((await api(page, 'POST', `/api/customers/${first.id}/deactivate`, { expectedVersion: updated.version })).status).toBe(200)
  const query = async (params: Record<string, string>) => {
    const result = await api<{ items: { id: string; legalName: string; tradeName: string; isActive: boolean; createdAtUtc: string; updatedAtUtc: string }[]; totalCount: number; page: number; pageSize: number }>(
      page, 'GET', `/api/customers?${new URLSearchParams(params)}`)
    expect(result.status).toBe(200)
    return result.body
  }
  const all = await query({ search: marker, pageSize: '100' })
  expect(all.totalCount).toBe(27)
  expect((await query({ search: `${marker} 00` })).items.map(item => item.id)).toEqual([first.id])
  expect((await query({ search: `${marker} Trade 26` })).items.map(item => item.id)).toEqual([first.id])
  expect((await query({ search: (await getCustomer(page, first.id)).cnpj })).items.map(item => item.id)).toEqual([first.id])
  expect((await query({ search: `${marker} Contact` })).items.map(item => item.id)).toEqual([first.id])
  expect((await query({ search: contactEmail })).items.map(item => item.id)).toEqual([first.id])
  const active = await query({ search: marker, isActive: 'true', pageSize: '100' })
  expect(active.totalCount).toBe(26)
  expect(active.items.every(item => item.isActive)).toBe(true)
  const inactive = await query({ search: marker, isActive: 'false' })
  expect(inactive.items.map(item => item.id)).toEqual([first.id])
  for (const [sort, field] of [['legalName', 'legalName'], ['tradeName', 'tradeName']] as const) {
    const ascending = (await query({ search: marker, sort, direction: 'asc', pageSize: '100' })).items.map(item => item[field])
    const descending = (await query({ search: marker, sort, direction: 'desc', pageSize: '100' })).items.map(item => item[field])
    expect(ascending).toEqual([...ascending].sort())
    expect(descending).toEqual([...ascending].reverse())
  }
  for (const [sort, field] of [['createdAt', 'createdAtUtc'], ['updatedAt', 'updatedAtUtc']] as const) {
    const ascending = (await query({ search: marker, sort, direction: 'asc', pageSize: '100' })).items
    const descending = (await query({ search: marker, sort, direction: 'desc', pageSize: '100' })).items
    const compare = (a: typeof ascending[number], b: typeof ascending[number]) =>
      a[field].localeCompare(b[field]) || a.id.localeCompare(b.id)
    expect(ascending.map(item => item.id)).toEqual([...ascending].sort(compare).map(item => item.id))
    expect(descending.map(item => item.id)).toEqual([...ascending].sort((a, b) => -compare(a, b)).map(item => item.id))
  }
  const pageOne = await query({ search: marker, page: '1', pageSize: '25' })
  const pageTwo = await query({ search: marker, page: '2', pageSize: '25' })
  expect(pageOne.totalCount).toBe(27)
  expect(pageOne.items).toHaveLength(25)
  expect(pageTwo.items).toHaveLength(2)
  expect(new Set([...pageOne.items, ...pageTwo.items].map(item => item.id)).size).toBe(27)
  expect((await query({ search: marker, pageSize: '999' })).pageSize).toBe(100)
  expect((await query({ search: marker, page: '2147483647', pageSize: '100' })).items).toHaveLength(0)
  expect((await query({ search: marker, page: '-5' })).page).toBe(1)
  expect((await query({ search: `${marker} NO MATCH` })).totalCount).toBe(0)
  expect((await query({ search: `${marker} %_!` })).totalCount).toBe(0)
  const longSearch = await api<{ errors: Record<string, string[]> }>(page, 'GET', `/api/customers?search=${'x'.repeat(201)}`)
  expect(longSearch.status).toBe(400)
  expect(longSearch.body.errors.search[0]).toMatch(/200/)
})

test('Concurrent duplicate CNPJ creation has one winner and a safe field conflict', async ({ newIsolatedPage }) => {
  const first = await newIsolatedPage()
  const second = await newIsolatedPage()
  try {
    await readyAdmin(first)
    await readyAdmin(second)
    const seed = freshSeed()
    const [a, b] = await Promise.all([
      api<{ id?: string; errors?: Record<string, string[]> }>(first, 'POST', '/api/customers', customerInput(seed)),
      api<{ id?: string; errors?: Record<string, string[]> }>(second, 'POST', '/api/customers', customerInput(seed, { cnpj: cnpj(seed).replace(/(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})/, '$1.$2.$3/$4-$5') }))
    ])
    expect([a.status, b.status].sort()).toEqual([201, 409])
    const rejected = a.status === 409 ? a : b
    expect(rejected.body.errors?.cnpj?.[0]).toMatch(/CNPJ/)
    expect(JSON.stringify(rejected.body)).not.toMatch(/Npgsql|PostgresException|DbUpdateException/i)
    const list = await api<{ totalCount: number }>(first, 'GET', `/api/customers?search=${cnpj(seed)}`)
    expect(list.body.totalCount).toBe(1)
  } finally { await first.close(); await second.close() }
})

test('Stale child mutation rejects the write and preserves the first change', async ({ newIsolatedPage }) => {
  const first = await newIsolatedPage()
  const second = await newIsolatedPage()
  try {
    await readyAdmin(first)
    await readyAdmin(second)
    const customer = await createCustomer(first, freshSeed())
    const created = await api<{ contact: { id: string }; version: string }>(first, 'POST', `/api/customers/${customer.id}/contacts`,
      { ...contactInput(), expectedVersion: customer.version })
    expect(created.status).toBe(201)
    const staleVersion = created.body.version
    const contactPath = `/api/customers/${customer.id}/contacts/${created.body.contact.id}`
    const winner = await api(first, 'PUT', contactPath, { ...contactInput({ name: 'Winner Contact' }), expectedVersion: staleVersion })
    expect(winner.status).toBe(200)
    const loser = await api(second, 'PUT', contactPath, { ...contactInput({ name: 'Loser Contact' }), expectedVersion: staleVersion })
    expect(loser.status).toBe(409)
    expect((await getCustomer(second, customer.id)).contacts[0].name).toBe('Winner Contact')
  } finally { await first.close(); await second.close() }
})
