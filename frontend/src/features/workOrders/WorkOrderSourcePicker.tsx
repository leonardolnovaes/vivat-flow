import { useCallback, useEffect, useState } from 'react'
import { ApiError } from '../../api'
import { LoadingState } from '../../components/LoadingState'
import { listWorkOrderSources } from './workOrderApi'
import type { WorkOrderSource, WorkOrderSourceList, WorkOrderSourceSummary } from './types'

type Props = { go: (path: string) => void; onSessionExpired: () => void }

export function WorkOrderSourcePicker({ go, onSessionExpired }: Props) {
  const [type, setType] = useState<WorkOrderSource>('Quote')
  const [input, setInput] = useState(''), [search, setSearch] = useState(''), [page, setPage] = useState(1)
  const [data, setData] = useState<WorkOrderSourceList | null>(null), [error, setError] = useState('')
  const load = useCallback(async () => {
    setData(null); setError('')
    const query = new URLSearchParams({ sourceType: type, page: String(page), pageSize: '20' })
    if (search) query.set('search', search)
    try { setData(await listWorkOrderSources(query)) }
    catch (caught) {
      if (caught instanceof ApiError && caught.status === 401) onSessionExpired()
      else setError(caught instanceof ApiError && caught.status === 403 ? 'Você não tem permissão para escolher origens de OS.' : 'Não foi possível carregar as origens. Tente novamente.')
    }
  }, [type, page, search, onSessionExpired])
  useEffect(() => { void load() }, [load])
  const changeType = (next: WorkOrderSource) => { setType(next); setPage(1); setInput(''); setSearch('') }
  const applySearch = () => { setPage(1); setSearch(input.trim()) }
  return <>
    <div className="page-title"><div><p className="eyebrow">Operação</p><h2>Nova Ordem de Serviço</h2><p>Escolha um orçamento aprovado ou contrato ativo para iniciar um rascunho operacional.</p></div></div>
    <section className="card wo-source-toolbar" aria-label="Buscar origem da OS">
      <fieldset><legend>Origem *</legend><div className="actions"><label><input type="radio" name="sourceType" checked={type === 'Quote'} onChange={() => changeType('Quote')}/> Orçamento aprovado</label><label><input type="radio" name="sourceType" checked={type === 'Contract'} onChange={() => changeType('Contract')}/> Contrato ativo</label></div></fieldset>
      <form onSubmit={event => { event.preventDefault(); applySearch() }}><label>Buscar por cliente ou número do orçamento<input value={input} maxLength={120} onChange={event => setInput(event.target.value)}/></label><button>Buscar</button>{search && <button type="button" className="link-button" onClick={() => { setInput(''); setSearch(''); setPage(1) }}>Limpar busca</button>}</form>
    </section>
    {error ? <section className="card error-panel"><p role="alert">{error}</p><button className="secondary" onClick={() => void load()}>Tentar novamente</button></section> : !data ? <LoadingState /> : data.items.length === 0 ? <section className="card empty-state"><h3>Nenhuma origem encontrada</h3><p>{search ? 'Tente outro nome ou número de orçamento.' : type === 'Quote' ? 'Não há orçamentos aprovados disponíveis nesta organização.' : 'Não há contratos ativos disponíveis nesta organização.'}</p></section> : <section className="wo-source-results" aria-label="Origens disponíveis">{data.items.map(source => <SourceCard key={`${source.sourceType}-${source.id}`} source={source} go={go}/>)}</section>}
    {data && data.totalCount > data.pageSize && <div className="pagination"><button className="secondary" disabled={page === 1} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {data.page} de {Math.ceil(data.totalCount / data.pageSize)}</span><button className="secondary" disabled={page * data.pageSize >= data.totalCount} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}
  </>
}

function SourceCard({ source, go }: { source: WorkOrderSourceSummary; go: Props['go'] }) {
  return <article className="card wo-source-card"><div><small>{source.sourceType === 'Quote' ? 'Orçamento aprovado' : 'Contrato ativo'} · {source.reference}</small><h3>{source.customerLegalNameSnapshot}</h3><p>{source.serviceAddressSnapshot || 'Local do serviço não informado'}</p></div><div className="wo-source-action">{source.currentWorkOrderId ? <><p>OS atual: {source.currentWorkOrderNumber}</p><button className="secondary" onClick={() => go(`/ordens-servico/${source.currentWorkOrderId}`)}>Ver OS</button></> : source.governingContractId ? <><p>{source.governingContractStatus === 'Active' ? 'Este escopo é executado pelo contrato ativo.' : 'Contrato em formalização. Aguarde a ativação para criar a OS.'}</p>{source.governingContractStatus === 'Active' && <button className="secondary" onClick={() => go(`/ordens-servico/novo?contractId=${source.governingContractId}`)}>Usar contrato</button>}</> : source.canCreate ? <button onClick={() => go(`/ordens-servico/novo?${source.sourceType === 'Quote' ? `quoteId=${source.id}` : `contractId=${source.id}`}`)}>Selecionar origem</button> : <p>Origem ainda não está pronta para uma OS. Verifique local e serviços.</p>}</div></article>
}
