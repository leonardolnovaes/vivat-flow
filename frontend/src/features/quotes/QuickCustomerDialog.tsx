import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../../api'
import { createContact, createCustomer, createUnit, getCustomer, updateContact } from '../customers/customerApi'
import type { Contact, Customer, Unit, UnitInput } from '../customers/types'

type Props = {
  customerId?: string
  close: () => void
  saved: (customer: Customer, unitId?: string) => void | Promise<void>
}

const emptyUnit: UnitInput = { name: '', street: '', number: '', complement: '', district: '', city: '', stateCode: '', postalCode: '', isPrimary: true }

export function QuickCustomerDialog({ customerId, close, saved }: Props) {
  const [createdId, setCreatedId] = useState(customerId)
  const [customer, setCustomer] = useState<Customer | null>(null)
  const [legalName, setLegalName] = useState('')
  const [cnpj, setCnpj] = useState('')
  const [addUnit, setAddUnit] = useState(Boolean(customerId))
  const [unit, setUnit] = useState<UnitInput>(emptyUnit)
  const [addContact, setAddContact] = useState(Boolean(customerId))
  const [contact, setContact] = useState({ name: '', email: '', phone: '' })
  const [customerPersisted, setCustomerPersisted] = useState(false)
  const [contactPersisted, setContactPersisted] = useState(false)
  const [unitPersisted, setUnitPersisted] = useState(false)
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [pending, setPending] = useState(false)
  useEffect(() => { if (customerId) void getCustomer(customerId).then(loaded => { setCustomer(loaded); setAddUnit(!loaded.units.some(item => item.isActive)); setAddContact(!loaded.contacts.some(item => item.isActive && item.email)); const incomplete = loaded.contacts.find(item => item.isActive && !item.email); if (incomplete) setContact({ name: incomplete.name, email: '', phone: incomplete.phone ?? '' }) }).catch(() => setErrors({ customer: ['Não foi possível carregar o cliente.'] })) }, [customerId])

  const save = async (event: FormEvent) => {
    event.preventDefault()
    if (pending || (customerId && !customer)) return
    setPending(true); setErrors({})
    let current = customer
    let unitId: string | undefined
    let persisted = customerPersisted || contactPersisted || unitPersisted
    try {
      if (!current) {
        if (createdId) current = await getCustomer(createdId)
        else { const created = await createCustomer({ legalName, cnpj, tradeName: '', notes: '' }); setCreatedId(created.id); setCustomer(created); setCustomerPersisted(true); current = created; persisted = true }
      }
      if (addContact && !contactPersisted) {
        const incomplete = current.contacts.find(item => item.isActive && !item.email)
        const response = incomplete
          ? await updateContact(current.id, incomplete.id, { ...contact, roleOrDepartment: incomplete.roleOrDepartment ?? '', isPrimary: incomplete.isPrimary }, current.version) as { contact: Contact; version: string }
          : await createContact(current.id, { ...contact, roleOrDepartment: '', isPrimary: !current.contacts.some(item => item.isActive && item.isPrimary) }, current.version) as { contact: Contact; version: string }
        persisted = true
        current = { ...current, version: response.version, contacts: [...current.contacts.filter(item => item.id !== response.contact.id), response.contact] }
        setCustomer(current)
        setContactPersisted(true)
      }
      if (addUnit && !unitPersisted) {
        const response = await createUnit(current.id, { ...unit, isPrimary: !current.units.some(item => item.isActive && item.isPrimary) }, current.version) as { unit: Unit; version: string }
        persisted = true
        unitId = response.unit.id
        current = { ...current, version: response.version, units: [...current.units.filter(item => item.id !== response.unit.id), response.unit] }
        setCustomer(current)
        setUnitPersisted(true)
      }
    } catch (caught) {
      if (caught instanceof ApiError) {
        const existing = caught.data.existingCustomer as { id: string; isActive: boolean } | undefined
        if (caught.status === 409 && existing?.isActive && !createdId) {
          try {
            const loaded = await getCustomer(existing.id)
            setCustomer(loaded); setCreatedId(loaded.id)
            setErrors({ form: ['Este CNPJ já está cadastrado. O cliente existente foi selecionado; confira os dados complementares e salve novamente.'] })
          } catch {
            setCreatedId(existing.id)
            setErrors({ form: ['Este CNPJ já está cadastrado, mas não foi possível carregar o cliente. Tente salvar novamente.'] })
          }
        } else if (persisted) setErrors({ form: ['Parte dos dados foi salva, mas não foi possível concluir o cadastro. Confira os dados e tente novamente.'] })
        else setErrors(Object.keys(caught.errors).length ? caught.errors : { form: [caught.message || 'Não foi possível salvar o cliente.'] })
      }
      else setErrors({ form: [persisted ? 'Parte dos dados foi salva, mas não foi possível concluir o cadastro. Confira os dados e tente novamente.' : 'Não foi possível salvar o cliente. Tente novamente.'] })
      setPending(false)
      return
    }

    try {
      if (current) await saved(current, unitId)
    } catch (caught) {
      const message = caught instanceof Error && caught.message.startsWith('Os dados')
        ? caught.message
        : 'Os dados foram salvos, mas não foi possível atualizar a tela. Tente recarregar.'
      setErrors({ form: [message] })
    } finally { setPending(false) }
  }
  return <div className="modal-backdrop"><section className="card modal quick-customer-dialog" role="dialog" aria-modal="true" aria-labelledby="quick-customer-title"><h2 id="quick-customer-title">{customerId ? 'Completar cadastro do cliente' : 'Cadastrar cliente rapidamente'}</h2><form onSubmit={save}>
    {!customerId && <><label>Razão social *<input value={legalName} required disabled={pending || Boolean(createdId)} onChange={event => setLegalName(event.target.value)}/>{errors.legalName?.map(item => <small className="error" key={item}>{item}</small>)}</label><label>CNPJ *<input value={cnpj} required disabled={pending || Boolean(createdId)} onChange={event => setCnpj(event.target.value)}/>{errors.cnpj?.map(item => <small className="error" key={item}>{item}</small>)}</label></>}
    {!customerId && <label className="checkbox"><input type="checkbox" checked={addContact} disabled={pending} onChange={event => setAddContact(event.target.checked)}/>Adicionar contato para aprovação</label>}
    {addContact && <div className="quick-customer-fields"><label>Nome do contato *<input value={contact.name} required disabled={pending || contactPersisted} onChange={event => setContact(value => ({ ...value, name: event.target.value }))}/></label><label>E-mail *<input type="email" value={contact.email} required disabled={pending || contactPersisted} onChange={event => setContact(value => ({ ...value, email: event.target.value }))}/></label><label>Telefone<input value={contact.phone} disabled={pending || contactPersisted} onChange={event => setContact(value => ({ ...value, phone: event.target.value }))}/></label>{['name','email','phone'].flatMap(key => errors[key]?.map(item => <small className="error" key={`${key}-${item}`}>{item}</small>) ?? [])}</div>}
    {!customerId && <label className="checkbox"><input type="checkbox" checked={addUnit} disabled={pending} onChange={event => setAddUnit(event.target.checked)}/>Adicionar unidade/local do serviço</label>}
    {addUnit && <div className="quick-customer-fields"><label>Nome da unidade *<input value={unit.name} required disabled={pending || unitPersisted} onChange={event => setUnit(value => ({ ...value, name: event.target.value }))}/></label><label>Rua *<input value={unit.street} required disabled={pending || unitPersisted} onChange={event => setUnit(value => ({ ...value, street: event.target.value }))}/></label><label>Número *<input value={unit.number} required disabled={pending || unitPersisted} onChange={event => setUnit(value => ({ ...value, number: event.target.value }))}/></label><label>Cidade *<input value={unit.city} required disabled={pending || unitPersisted} onChange={event => setUnit(value => ({ ...value, city: event.target.value }))}/></label><label>UF *<input value={unit.stateCode} maxLength={2} required disabled={pending || unitPersisted} onChange={event => setUnit(value => ({ ...value, stateCode: event.target.value.toUpperCase() }))}/></label>{['name','street','number','city','stateCode'].flatMap(key => errors[key]?.map(item => <small className="error" key={`${key}-${item}`}>{item}</small>) ?? [])}</div>}
    {errors.form?.map(item => <p className="error" role="alert" key={item}>{item}</p>)}{errors.customer?.map(item => <p className="error" role="alert" key={item}>{item}</p>)}
    <div className="actions"><button disabled={pending || Boolean(customerId && !customer)}>{pending ? 'Salvando...' : customerId && (contactPersisted || unitPersisted) && (!addContact || contactPersisted) && (!addUnit || unitPersisted) ? 'Tentar atualizar tela' : customerId ? 'Salvar dados do cliente' : 'Cadastrar cliente'}</button><button className="secondary" type="button" disabled={pending} onClick={close}>Cancelar</button></div>
  </form></section></div>
}
