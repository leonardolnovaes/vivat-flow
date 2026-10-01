import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../../api'
import { getCustomer } from '../customers/customerApi'
import type { Contact } from '../customers/types'
import { sendForApproval } from './quoteApi'
import type { Quote } from './types'

type Props = { quote: Quote; close: () => void; saved: () => Promise<void>; failure: (text: string) => void }

export function QuoteSendDialog({ quote, close, saved, failure }: Props) {
  const [contacts, setContacts] = useState<Contact[]>([])
  const [selected, setSelected] = useState<string[]>([])
  const [validUntil, setValidUntil] = useState('')
  const [pending, setPending] = useState(false)
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  useEffect(() => { void getCustomer(quote.customerId).then(customer => setContacts(customer.contacts.filter(contact => contact.isActive && Boolean(contact.email)))).catch(() => setErrors({ contacts: ['Não foi possível carregar os contatos do cliente.'] })) }, [quote.customerId])
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (pending) return
    if (!selected.length) { setErrors({ recipientContactIds: ['Selecione ao menos um contato para aprovação.'] }); return }
    setPending(true); setErrors({})
    try { await sendForApproval(quote.id, quote.version, { recipientContactIds: selected, validUntil }); await saved() }
    catch (caught) { if (caught instanceof ApiError && Object.keys(caught.errors).length) setErrors(caught.errors); else failure('Não foi possível enviar o orçamento.') }
    finally { setPending(false) }
  }
  return <div className="modal-backdrop"><section className="card modal" role="dialog" aria-modal="true" aria-labelledby="quote-send-title"><h2 id="quote-send-title">Enviar para aprovação</h2><p>O envio registra os destinatários previstos e a espera pela resposta do cliente. O e-mail será enviado fora do Vivat Flow.</p><form onSubmit={submit}><fieldset><legend>Contatos destinatários *</legend>{contacts.length ? contacts.map(contact => <label className="checkbox" key={contact.id}><input type="checkbox" checked={selected.includes(contact.id)} disabled={pending} onChange={event => setSelected(current => event.target.checked ? [...current, contact.id] : current.filter(id => id !== contact.id))}/>{contact.name} · {contact.email} · {contact.phone || 'Sem telefone'}</label>) : <p>Nenhum contato ativo com e-mail cadastrado.</p>}{errors.recipientContactIds?.map(item => <small className="error" role="alert" key={item}>{item}</small>)}{errors.contacts?.map(item => <small className="error" role="alert" key={item}>{item}</small>)}</fieldset><label>Válido até<input type="date" value={validUntil} disabled={pending} onChange={event => setValidUntil(event.target.value)}/>{errors.validUntil?.map(item => <small className="error" role="alert" key={item}>{item}</small>)}</label><div className="actions"><button disabled={pending || !contacts.length}>{pending ? 'Enviando...' : 'Enviar para aprovação'}</button><button type="button" className="secondary" disabled={pending} onClick={close}>Cancelar</button></div></form></section></div>
}
