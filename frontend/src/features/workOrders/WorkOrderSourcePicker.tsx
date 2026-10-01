import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../../api'
import { LoadingState } from '../../components/LoadingState'
import { listWorkOrderSources } from './workOrderApi'
import type { WorkOrderSource, WorkOrderSourceList, WorkOrderSourceSummary } from './types'

type Props = { go: (path: string) => void; onSessionExpired: () => void }

export function WorkOrderSourcePicker({ go, onSessionExpired }: Props) {
  const type: WorkOrderSource = 'Contract'
  const [input, setInput] = useState(''), [search, setSearch] = useState(''), [page, setPage] = useState(1)
  const [data, setData] = useState<WorkOrderSourceList | null>(null), [error, setError] = useState('')
  const requestId = useRef(0)
  const load = useCallback(async () => {
    const currentRequest = ++requestId.current
    setData(null); setError('')
    const query = new URLSearchParams({ sourceType: type, page: String(page), pageSize: '20' })
    if (search) query.set('search', search)
    try {
      const result = await listWorkOrderSources(query)
      if (currentRequest === requestId.current) setData(result)
    }
    catch (caught) {
      if (currentRequest !== requestId.current) return
      if (caught instanceof ApiError && caught.status === 401) onSessionExpired()
      else setError(caught instanceof ApiError && caught.status === 403 ? 'Você não tem permissão para selecionar contratos.' : 'Não foi possível carregar os contratos. Tente novamente.')
    }
  }, [type, page, search, onSessionExpired])
  useEffect(() => { void load() }, [load])
  const applySearch = () => { setPage(1); setSearch(input.trim()) }
  return <>
    <div className="page-title"><div><p className="eyebrow">Operação</p><h2>Nova Ordem de Serviço</h2><p>Escolha um contrato ativo para iniciar um rascunho operacional.</p></div></div>
    <section className="card wo-source-toolbar" aria-label="Buscar contrato para a OS">
      <form onSubmit={event => { event.preventDefault(); applySearch() }}><label>Buscar por cliente ou número do contrato<input value={input} maxLength={120} onChange={event => setInput(event.target.value)}/></label><button>Buscar</button>{search && <button type="button" className="link-button" onClick={() => { setInput(''); setSearch(''); setPage(1) }}>Limpar busca</button>}</form>
    </section>
    {error ? <section className="card error-panel"><p role="alert">{error}</p><button className="secondary" onClick={() => void load()}>Tentar novamente</button></section> : !data ? <LoadingState /> : data.items.length === 0 ? <section className="card empty-state"><h3>Nenhum contrato ativo encontrado</h3><p>{search ? 'Tente outro nome ou número de contrato.' : 'Não há contratos ativos disponíveis nesta organização.'}</p></section> : <section className="wo-source-results" aria-label="Contratos disponíveis">{data.items.map(source => <SourceCard key={`${source.sourceType}-${source.id}`} source={source} go={go}/>)}</section>}
    {data && data.totalCount > data.pageSize && <div className="pagination"><button className="secondary" disabled={page === 1} onClick={() => setPage(value => value - 1)}>Anterior</button><span>Página {data.page} de {Math.ceil(data.totalCount / data.pageSize)}</span><button className="secondary" disabled={page * data.pageSize >= data.totalCount} onClick={() => setPage(value => value + 1)}>Próxima</button></div>}
  </>
}

function SourceCard({ source, go }: { source: WorkOrderSourceSummary; go: Props['go'] }) {
  return <article className="card wo-source-card"><div><small>Contrato ativo · {source.reference}</small><h3>{source.customerLegalNameSnapshot}</h3><p>{source.serviceAddressSnapshot || 'Local do serviço não informado'}</p></div><div className="wo-source-action">{source.currentWorkOrderId ? <><p>OS atual: {source.currentWorkOrderNumber}</p><button className="secondary" onClick={() => go(`/ordens-servico/${source.currentWorkOrderId}`)}>Ver OS</button></> : source.canCreate ? <button onClick={() => go(`/ordens-servico/novo?contractId=${source.id}`)}>Selecionar contrato</button> : <p>O contrato ainda não está pronto para uma nova OS. Verifique local, serviços e execuções em aberto.</p>}</div></article>
}
