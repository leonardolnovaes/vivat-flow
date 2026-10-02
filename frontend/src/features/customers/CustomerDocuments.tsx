/* oxlint-disable react-hooks/exhaustive-deps, react/set-state-in-effect -- Effects synchronize customer-scoped API data. */
import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError } from '../../api'
import { LoadingState } from '../../components/LoadingState'
import { downloadDocument, listDocumentContexts, listDocuments, uploadDocument } from './documentApi'
import type { ContextOption, CustomerDocument, DocumentCategory, DocumentList, DocumentPurpose } from './documentApi'

const categories: Record<DocumentCategory, string> = {
  General: 'Geral', Report: 'Relatório', Certificate: 'Certificado', Contract: 'Contrato',
  Evidence: 'Evidência', Photo: 'Foto', SignedDocument: 'Documento assinado', Other: 'Outro'
}
const purposes: Record<DocumentPurpose, string> = {
  InternalSupporting: 'Arquivo interno / apoio', CustomerDeliverable: 'Entrega ao cliente'
}
const fileTypes = '.pdf,.png,.jpg,.jpeg,.webp'

function problem(error: unknown, expired: () => void, fallback: string) {
  if (error instanceof ApiError) {
    if (error.status === 401) { expired(); return 'Sua sessão expirou. Entre novamente para continuar.' }
    if (error.status === 403) return 'Seu perfil não permite esta operação.'
    if (error.status === 404) return 'Este cliente ou documento não está disponível para seu perfil.'
    if (error.status === 400 || error.status === 422) return error.message || fallback
  }
  return fallback
}

export function CustomerDocuments({ customerId, roles, onSessionExpired }: { customerId: string; roles: string[]; onSessionExpired: () => void }) {
  const canUpload = roles.includes('ADMIN') || roles.includes('MANAGER')
  const isAdmin = roles.includes('ADMIN')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<DocumentList | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [downloadError, setDownloadError] = useState('')
  const [notice, setNotice] = useState('')
  const [downloadId, setDownloadId] = useState<string | null>(null)
  const [uploadOpen, setUploadOpen] = useState(false)
  const requestId = useRef(0)
  const load = async (targetPage = page) => {
    const current = ++requestId.current
    setLoading(true); setError(''); setData(null)
    try { const result = await listDocuments(customerId, targetPage); if (current === requestId.current) setData(result) }
    catch (caught) { if (current === requestId.current) setError(problem(caught, onSessionExpired, 'Não foi possível carregar os documentos.')) }
    finally { if (current === requestId.current) setLoading(false) }
  }
  useEffect(() => { void load(); const requests = requestId; return () => { requests.current++ } }, [customerId, page])
  const download = async (document: CustomerDocument) => {
    setDownloadId(document.id); setDownloadError('')
    try { await downloadDocument(document) }
    catch (caught) { setDownloadError(problem(caught, onSessionExpired, 'Não foi possível baixar o documento. Tente novamente.')) }
    finally { setDownloadId(null) }
  }
  const uploaded = () => { setUploadOpen(false); setNotice('Documento enviado com sucesso.'); if (page === 1) void load(1); else setPage(1) }
  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1
  return <section className="card section-card customer-documents" aria-labelledby="customer-documents-title">
    <div className="section-heading"><div><h3 id="customer-documents-title">Documentos</h3><p>Arquivos relacionados a este cliente e aos seus serviços.</p></div>{canUpload && <button onClick={() => { setNotice(''); setUploadOpen(true) }}>Enviar documento</button>}</div>
    {notice && <p className="notice" role="status">{notice}</p>}
    {error && <div className="error-panel" role="alert"><p>{error}</p><button className="secondary" onClick={() => void load()}>Tentar novamente</button></div>}
    {downloadError && <p className="error" role="alert">{downloadError}</p>}
    {loading && !data ? <LoadingState /> : data && <>
      {data.items.length === 0 ? <div className="empty-state" role="status"><h4>Nenhum documento cadastrado para este cliente.</h4><p>Os arquivos enviados aparecerão aqui.</p>{canUpload && <button onClick={() => setUploadOpen(true)}>Enviar documento</button>}</div> : <div className="document-list">{data.items.map(document => <article className="document-entry" key={document.id}>
        <div className="document-entry-main"><strong>{document.fileName}</strong><span className={`document-purpose ${document.purpose === 'CustomerDeliverable' ? 'document-purpose-deliverable' : ''}`}>{purposes[document.purpose] ?? 'Finalidade não informada'}</span></div>
        <div className="document-entry-meta"><span>{categories[document.category] ?? 'Outra categoria'}</span><span>{document.contextLabel || 'Cliente'}</span><span>{new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(document.uploadedAtUtc))}</span><span>Enviado por {document.uploadedByName || 'usuário indisponível'}</span><span>{formatSize(document.sizeBytes)}</span></div>
        {document.description && <p className="document-description">{document.description}</p>}
        <button type="button" className="secondary" disabled={downloadId === document.id} onClick={() => void download(document)}>{downloadId === document.id ? 'Baixando…' : 'Baixar'}</button>
      </article>)}</div>}
      {data.totalCount > data.pageSize && <div className="pagination"><span>Página {data.page} de {totalPages} · {data.totalCount} documentos</span><button className="secondary" disabled={loading || page <= 1} onClick={() => setPage(page - 1)}>Anterior</button><button className="secondary" disabled={loading || page >= totalPages} onClick={() => setPage(page + 1)}>Próxima</button></div>}
    </>}
    {uploadOpen && <UploadDialog customerId={customerId} isAdmin={isAdmin} onSessionExpired={onSessionExpired} close={() => setUploadOpen(false)} uploaded={uploaded} />}
  </section>
}

function formatSize(bytes: number) { return bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} MB` }

function UploadDialog({ customerId, isAdmin, onSessionExpired, close, uploaded }: { customerId: string; isAdmin: boolean; onSessionExpired: () => void; close: () => void; uploaded: () => void }) {
  const [file, setFile] = useState<File | null>(null)
  const [category, setCategory] = useState<DocumentCategory>('General')
  const [purpose, setPurpose] = useState<DocumentPurpose>('InternalSupporting')
  const [description, setDescription] = useState('')
  const [contextKey, setContextKey] = useState('')
  const [contexts, setContexts] = useState<ContextOption[]>([])
  const [contextsLoading, setContextsLoading] = useState(true)
  const [contextError, setContextError] = useState('')
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const [message, setMessage] = useState('')
  const [pending, setPending] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)
  useEffect(() => {
    let active = true
    listDocumentContexts(customerId).then(value => { if (active) setContexts(value) })
      .catch(caught => { if (active) setContextError(problem(caught, onSessionExpired, 'Não foi possível carregar os contextos disponíveis.')) })
      .finally(() => { if (active) setContextsLoading(false) })
    fileInput.current?.focus()
    return () => { active = false }
  }, [customerId, onSessionExpired])
  const submit = async (event: FormEvent) => {
    event.preventDefault(); if (pending) return
    const local: Record<string, string[]> = {}
    if (!file) local.file = ['Selecione um arquivo.']
    else if (!/\.(pdf|png|jpe?g|webp)$/i.test(file.name)) local.file = ['Envie um PDF ou imagem PNG, JPEG ou WebP.']
    if (description.length > 1000) local.description = ['Use no máximo 1.000 caracteres.']
    setErrors(local); if (Object.keys(local).length) return
    setPending(true); setMessage('')
    try {
      const context = contextsLoading || Boolean(contextError) ? null : (contexts.find(item => `${item.type}:${item.id}` === contextKey) ?? null)
      await uploadDocument(customerId, { file: file!, category, purpose, description, context })
      uploaded()
    } catch (caught) {
      setErrors(caught instanceof ApiError ? caught.errors : {})
      setMessage(problem(caught, onSessionExpired, 'Não foi possível enviar o documento. Tente novamente.'))
    } finally { setPending(false) }
  }
  const available = (Object.keys(categories) as DocumentCategory[]).filter(item => isAdmin || (item !== 'Contract' && item !== 'SignedDocument'))
  return <div className="modal-backdrop"><section className="card modal document-upload-modal" role="dialog" aria-modal="true" aria-labelledby="document-upload-title">
    <h2 id="document-upload-title">Enviar documento</h2><p>Selecione um arquivo PDF ou imagem PNG, JPEG ou WebP.</p>
    {message && <p className="error" role="alert">{message}</p>}
    <form noValidate onSubmit={submit}>
      <label>Arquivo *<input ref={fileInput} type="file" accept={fileTypes} disabled={pending} onChange={event => setFile(event.target.files?.[0] ?? null)} />{errors.file && <small className="error" role="alert">{errors.file[0]}</small>}</label>
      <label>Categoria *<select value={category} disabled={pending} onChange={event => setCategory(event.target.value as DocumentCategory)}>{available.map(item => <option key={item} value={item}>{categories[item]}</option>)}</select>{errors.category && <small className="error" role="alert">{errors.category[0]}</small>}</label>
      <label>Finalidade *<select value={purpose} disabled={pending} onChange={event => setPurpose(event.target.value as DocumentPurpose)}>{(Object.keys(purposes) as DocumentPurpose[]).map(item => <option key={item} value={item}>{purposes[item]}</option>)}</select>{errors.purpose && <small className="error" role="alert">{errors.purpose[0]}</small>}</label>
      <label>Relacionado a<select value={contextKey} disabled={pending || contextsLoading || Boolean(contextError)} onChange={event => setContextKey(event.target.value)}><option value="">Sem contexto específico</option>{contexts.map(item => <option key={`${item.type}:${item.id}`} value={`${item.type}:${item.id}`}>{item.label}</option>)}</select>{errors.contextType && <small className="error" role="alert">{errors.contextType[0]}</small>}{errors.contextId && <small className="error" role="alert">{errors.contextId[0]}</small>}</label>
      {contextsLoading && <LoadingState size="sm" />}{contextError && <p className="error" role="alert">{contextError} Você ainda pode enviar o documento sem contexto específico.</p>}
      <label>Descrição<textarea value={description} maxLength={1000} disabled={pending} onChange={event => setDescription(event.target.value)} />{errors.description && <small className="error" role="alert">{errors.description[0]}</small>}</label>
      <div className="actions"><button type="button" className="secondary" disabled={pending} onClick={close}>Cancelar</button><button disabled={pending}>{pending ? 'Enviando…' : 'Enviar documento'}</button></div>
    </form>
  </section></div>
}
