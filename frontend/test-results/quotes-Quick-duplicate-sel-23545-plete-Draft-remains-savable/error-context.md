# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: quotes.spec.ts >> Quick duplicate selects the active customer and an incomplete Draft remains savable
- Location: e2e\quotes.spec.ts:65:1

# Error details

```
Error: expect(locator).toBeVisible() failed

Locator: getByText('Orçamento incompleto')
Expected: visible
Timeout: 5000ms
Error: element(s) not found

Call log:
  - Expect "toBeVisible" getByText('Orçamento incompleto') with timeout 5000ms
  - waiting for getByText('Orçamento incompleto')

```

```yaml
- complementary:
  - text: TSDT ERP
  - navigation "Navegação principal":
    - button "Clientes"
    - button "Serviços"
    - button "Orçamentos"
    - button "Administração"
  - strong: E2E Stable ADMIN
  - text: Administrador
  - button "Sair"
- main:
  - paragraph: Comercial · Duplicado Quote 492cf4a885f4-39508-14
  - heading "ORC-2026-000001" [level=2]
  - paragraph: Atualizado em 27/09/2026, 10:41
  - text: Rascunho
  - article:
    - heading "Cliente" [level=3]
    - term: Razão social
    - definition: Duplicado Quote 492cf4a885f4-39508-14
    - term: CNPJ
    - definition: 13.950.800/0013-59
    - term: Unidade/local
    - definition: Não informado
    - heading "Responsável pelo orçamento" [level=3]
    - term: Profissional
    - definition: Não definido
    - heading "Contexto comercial" [level=3]
    - term: Funcionários
    - definition: Não informado
    - term: Grau de risco
    - definition: Não informado
    - heading "Serviços" [level=3]
    - paragraph: Nenhum serviço informado.
    - heading "Condições comerciais" [level=3]
    - term: Valor total
    - definition: Não informado
    - term: Pagamento
    - definition: Não informado
    - heading "Observações" [level=3]
    - paragraph: Rascunho preservado
  - complementary:
    - heading "Situação comercial" [level=3]
    - text: Rascunho
    - paragraph: Revise e envie o orçamento para aprovação.
    - term: Enviado em
    - definition: —
    - term: Validade
    - definition: —
    - term: Retorno
    - definition: Nenhum retorno registrado
    - button "Editar"
    - button "Enviar para aprovação" [disabled]
    - button "Cancelar orçamento"
    - paragraph: Complete os dados obrigatórios antes de enviar para aprovação.
  - heading "Visita Técnica" [level=3]
  - paragraph: Agendamento e acompanhamento da visita vinculada ao orçamento.
  - button "Agendar visita"
  - heading "Nenhuma visita agendada" [level=4]
  - paragraph: Agende uma visita para registrar o atendimento técnico.
  - heading "Histórico do orçamento" [level=3]
  - list:
    - listitem:
      - strong: Orçamento criado
      - text: 27/09/2026, 10:41 · Usuário responsável
```

# Test source

```ts
  1   | import { expect, test } from './customers-runtime.fixture'
  2   | import type { Page } from '@playwright/test'
  3   | import { api, contactInput, createCustomer, freshSeed, readyAdmin, unique, unitInput } from './customers-hardening.helpers'
  4   | 
  5   | type Service = { id: string }
  6   | type UnitResponse = { unit: { id: string }; version: string }
  7   | type ContactResponse = { version: string }
  8   | 
  9   | async function selectCustomer(page: Page, customer: { id: string; legalName: string; cnpj: string }, term: string) {
  10  |   const search = page.getByLabel('Buscar cliente por razão social, nome fantasia ou CNPJ')
  11  |   await search.fill(term)
  12  |   await page.getByRole('option', { name: new RegExp(customer.legalName) }).click()
  13  |   await expect(search).toHaveValue(new RegExp(customer.legalName))
  14  | }
  15  | 
  16  | async function addUnit(page: Page, customerId: string, version: string) {
  17  |   const result = await api<UnitResponse>(page, 'POST', `/api/customers/${customerId}/units`, { ...unitInput({ isPrimary: true }), expectedVersion: version })
  18  |   expect(result.status, JSON.stringify(result.body)).toBe(201)
  19  |   return result.body
  20  | }
  21  | 
  22  | async function addContact(page: Page, customerId: string, version: string) {
  23  |   const result = await api<ContactResponse>(page, 'POST', `/api/customers/${customerId}/contacts`, { ...contactInput({ isPrimary: true }), expectedVersion: version })
  24  |   expect(result.status, JSON.stringify(result.body)).toBe(201)
  25  |   return result.body
  26  | }
  27  | 
  28  | test('Quote customer search supports legal name and both CNPJ forms, with active units', async ({ page }) => {
  29  |   await readyAdmin(page)
  30  |   const customer = await createCustomer(page, freshSeed(), { legalName: `Busca Quote ${unique()}`, tradeName: `Fantasia ${unique()}` })
  31  |   const unit = await addUnit(page, customer.id, customer.version)
  32  |   await page.goto('/orcamentos/novo')
  33  | 
  34  |   await selectCustomer(page, customer, customer.legalName)
  35  |   await expect(page.getByLabel('Unidade/local do serviço')).toContainText('E2E Unit')
  36  |   await page.getByRole('button', { name: 'Alterar' }).click()
  37  |   await expect(page.getByLabel('Buscar cliente por razão social, nome fantasia ou CNPJ')).toHaveValue('')
  38  |   await selectCustomer(page, customer, customer.cnpj.replace(/^(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})$/, '$1.$2.$3/$4-$5'))
  39  |   await page.getByLabel('Buscar cliente por razão social, nome fantasia ou CNPJ').fill('')
  40  |   await expect(page.getByLabel('Buscar cliente por razão social, nome fantasia ou CNPJ')).toHaveValue('')
  41  |   await expect(page.getByLabel('Unidade/local do serviço')).toBeDisabled()
  42  |   await expect(page.getByRole('button', { name: 'Alterar' })).toHaveCount(0)
  43  |   await selectCustomer(page, customer, customer.legalName)
  44  |   await page.getByRole('button', { name: 'Alterar' }).click()
  45  |   await selectCustomer(page, customer, customer.cnpj)
  46  |   await page.getByLabel('Unidade/local do serviço').selectOption(unit.unit.id)
  47  |   await expect(page.getByLabel('Unidade/local do serviço')).toHaveValue(unit.unit.id)
  48  | })
  49  | 
  50  | test('Quote unit state explains missing units and clears an old unit after customer switch', async ({ page }) => {
  51  |   await readyAdmin(page)
  52  |   const withUnit = await createCustomer(page, freshSeed(), { legalName: `Com unidade ${unique()}` })
  53  |   const unit = await addUnit(page, withUnit.id, withUnit.version)
  54  |   const withoutUnit = await createCustomer(page, freshSeed(), { legalName: `Sem unidade ${unique()}` })
  55  |   await page.goto('/orcamentos/novo')
  56  |   await selectCustomer(page, withUnit, withUnit.legalName)
  57  |   await page.getByLabel('Unidade/local do serviço').selectOption(unit.unit.id)
  58  |   await page.getByRole('button', { name: 'Alterar' }).click()
  59  |   await selectCustomer(page, withoutUnit, withoutUnit.legalName)
  60  |   await expect(page.getByLabel('Unidade/local do serviço')).toBeDisabled()
  61  |   await expect(page.getByRole('status')).toContainText('Este cliente não possui uma unidade ativa cadastrada.')
  62  |   await expect(page.getByRole('button', { name: 'Completar cadastro do cliente' })).toBeVisible()
  63  | })
  64  | 
  65  | test('Quick duplicate selects the active customer and an incomplete Draft remains savable', async ({ page }) => {
  66  |   await readyAdmin(page)
  67  |   const customer = await createCustomer(page, freshSeed(), { legalName: `Duplicado Quote ${unique()}` })
  68  |   await page.goto('/orcamentos/novo')
  69  |   await page.getByLabel('Observações').fill('Rascunho preservado')
  70  |   await page.getByRole('button', { name: '+ Cadastrar cliente rapidamente' }).click()
  71  |   await page.getByRole('dialog').getByLabel('Razão social *').fill('Tentativa duplicada')
  72  |   await page.getByRole('dialog').getByLabel('CNPJ *').fill(customer.cnpj)
  73  |   await page.getByRole('dialog').getByRole('button', { name: 'Cadastrar cliente' }).click()
  74  |   await expect(page.getByRole('dialog')).toHaveCount(0)
  75  |   await expect(page.getByText('Cliente selecionado com cadastro incompleto.')).toBeVisible()
  76  |   await expect(page.getByLabel('Observações')).toHaveValue('Rascunho preservado')
  77  |   const create = page.waitForResponse(response => response.url().includes('/api/quotes') && response.request().method() === 'POST' && response.status() === 201)
  78  |   await page.getByRole('button', { name: 'Salvar rascunho' }).click()
  79  |   await create
> 80  |   await expect(page.getByText('Orçamento incompleto')).toBeVisible()
      |                                                        ^ Error: expect(locator).toBeVisible() failed
  81  | })
  82  | 
  83  | test('Quote approval aggregates validation, services remain structurally separate, and complete quote reaches pending approval', async ({ page }) => {
  84  |   await readyAdmin(page)
  85  |   const incomplete = await createCustomer(page, freshSeed(), { legalName: `Incompleto Quote ${unique()}` })
  86  |   const serviceOne = await api<Service>(page, 'POST', '/api/services', { code: `PGR-${unique()}`, name: 'PPP Quote', description: null, basePrice: null })
  87  |   const serviceTwo = await api<Service>(page, 'POST', '/api/services', { code: `PCMSO-${unique()}`, name: 'PCMSO Quote', description: null, basePrice: null })
  88  |   const serviceThree = await api<Service>(page, 'POST', '/api/services', { code: `LTCAT-${unique()}`, name: 'LTCAT Quote', description: null, basePrice: null })
  89  |   expect(serviceOne.status).toBe(201); expect(serviceTwo.status).toBe(201); expect(serviceThree.status).toBe(201)
  90  |   await page.goto('/orcamentos/novo')
  91  |   await selectCustomer(page, incomplete, incomplete.legalName)
  92  |   for (const service of [serviceOne, serviceTwo, serviceThree]) {
  93  |     await page.getByLabel('Adicionar serviço').selectOption(service.body.id)
  94  |     await expect(page.getByLabel('Serviços adicionados').locator('.selected-service')).toHaveCount([serviceOne, serviceTwo, serviceThree].indexOf(service) + 1)
  95  |     await expect(page.getByLabel('Condições comerciais').locator('.selected-service')).toHaveCount(0)
  96  |   }
  97  |   await page.getByRole('button', { name: 'Salvar rascunho' }).click()
  98  |   await page.getByRole('button', { name: 'Enviar para aprovação' }).click()
  99  |   const alert = page.getByRole('alert')
  100 |   for (const text of ['Informe o valor total.', 'Informe a condição de pagamento.', 'Informe a quantidade de funcionários.', 'Informe o grau de risco.', 'Informe o endereço do serviço.', 'Informe um contato ativo para o cliente.', 'Informe uma unidade ativa para o cliente.']) await expect(alert).toContainText(text)
  101 |   await expect(page.getByRole('heading', { name: 'Cliente' })).toBeVisible()
  102 |   await expect(page.getByRole('heading', { name: 'Contexto comercial' })).toBeVisible()
  103 |   await expect(page.getByRole('heading', { name: 'Serviços' })).toBeVisible()
  104 |   await expect(page.getByRole('heading', { name: 'Condições comerciais' })).toBeVisible()
  105 |   await expect(page.getByRole('heading', { name: 'Observações' })).toBeVisible()
  106 | 
  107 |   const complete = await createCustomer(page, freshSeed(), { legalName: `Completo Quote ${unique()}` })
  108 |   const unit = await addUnit(page, complete.id, complete.version)
  109 |   await addContact(page, complete.id, unit.version)
  110 |   await page.goto('/orcamentos/novo')
  111 |   await selectCustomer(page, complete, complete.legalName)
  112 |   await page.getByLabel('Unidade/local do serviço').selectOption(unit.unit.id)
  113 |   await page.getByLabel('Quantidade de funcionários').fill('20')
  114 |   await page.getByLabel('Grau de risco').selectOption('Two')
  115 |   await page.getByLabel('Adicionar serviço').selectOption(serviceOne.body.id)
  116 |   await page.getByLabel('Valor total (R$)').fill('1000,00')
  117 |   await page.getByLabel('Condição de pagamento').selectOption('Cash')
  118 |   await page.getByRole('button', { name: 'Salvar rascunho' }).click()
  119 |   await page.getByRole('button', { name: 'Enviar para aprovação' }).click()
  120 |   await expect(page.getByText('Aguardando aprovação')).toBeVisible()
  121 | })
  122 | 
  123 | test('MANAGER and USER do not see Quotes and direct access is denied', async ({ page, newIsolatedPage }) => {
  124 |   await readyAdmin(page)
  125 |   for (const role of ['MANAGER', 'USER'] as const) {
  126 |     const isolated = await newIsolatedPage()
  127 |     const email = `quote-${role}-${unique()}@example.test`
  128 |     const created = await api<{ temporaryPassword: string }>(page, 'POST', '/api/admin/users', { fullName: `Quote ${role}`, email, role })
  129 |     expect(created.status).toBe(201)
  130 |     await isolated.goto('/')
  131 |     await api(isolated, 'POST', '/api/auth/login', { email, password: created.body.temporaryPassword })
  132 |     await api(isolated, 'POST', '/api/auth/change-password', { currentPassword: created.body.temporaryPassword, newPassword: 'E2eUser1!Password' })
  133 |     await isolated.goto('/orcamentos')
  134 |     await expect(isolated.getByRole('heading', { name: 'Acesso negado' })).toBeVisible()
  135 |     await isolated.context().close()
  136 |   }
  137 | })
  138 | 
  139 | test('Quote history presents changed fields in Portuguese without technical identifiers', async ({ page }) => {
  140 |   await readyAdmin(page)
  141 |   const customer = await createCustomer(page, freshSeed(), { legalName: `Histórico Quote ${unique()}` })
  142 |   const firstUnit = await addUnit(page, customer.id, customer.version)
  143 |   const secondUnit = await addUnit(page, customer.id, firstUnit.version)
  144 |   const service = await api<Service>(page, 'POST', '/api/services', { code: `HIST-${unique()}`, name: 'Serviço histórico', description: null, basePrice: null })
  145 |   expect(service.status).toBe(201)
  146 | 
  147 |   type QuoteMutation = { id: string; version: string; items: { id: string; serviceId: string }[] }
  148 |   const created = await api<QuoteMutation>(page, 'POST', '/api/quotes', {
  149 |     customerId: customer.id, items: [{ serviceId: service.body.id }], totalAmount: 100, paymentType: 'Cash', installmentCount: 1,
  150 |     employeeCount: 10, riskDegree: 'One', serviceUnitId: firstUnit.unit.id, notes: 'Antes da atualização'
  151 |   })
  152 |   expect(created.status).toBe(201)
  153 |   const updated = await api<QuoteMutation>(page, 'PUT', `/api/quotes/${created.body.id}`, {
  154 |     customerId: customer.id, items: created.body.items, totalAmount: 200, paymentType: 'Installments', installmentCount: 2,
  155 |     employeeCount: 20, riskDegree: 'Two', serviceUnitId: secondUnit.unit.id, notes: 'Depois da atualização', expectedVersion: created.body.version
  156 |   })
  157 |   expect(updated.status).toBe(200)
  158 | 
  159 |   await page.goto(`/orcamentos/${created.body.id}`)
  160 |   const history = page.locator('.quote-history')
  161 |   await expect(history).toContainText('Campos alterados: Valor total, Condição de pagamento, Quantidade de parcelas, Quantidade de funcionários, Grau de risco, Local do serviço e Observações.')
  162 |   for (const identifier of ['TotalAmount', 'PaymentType', 'InstallmentCount', 'EmployeeCount', 'RiskDegree', 'ServiceAddress', 'Notes']) {
  163 |     await expect(history).not.toContainText(identifier)
  164 |   }
  165 | })
  166 | 
```