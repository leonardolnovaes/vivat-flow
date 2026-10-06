import { useCallback, useEffect, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { ApiError, request } from '../../api'
import { LoadingState } from '../../components/LoadingState'
import { UserSettingsModal } from '../../components/UserSettingsModal'
import { useLocale } from '../../i18n/useLocale'
import type { SupportedLocale } from '../../i18n/locale'

type Status = 'Active' | 'Suspended' | 'Deactivated'
type Organization = { id: string; name: string; slug: string; status: Status; createdAtUtc: string; updatedAtUtc: string }
type OrganizationServiceLine = { id: string; code: string; name: string; isActive: boolean; isEnabled: boolean }
type Summary = { total: number; active: number; suspended: number; deactivated: number }
type Errors = Record<string, string[]>
type TenantAdministrator = { id: string; fullName: string; email: string; isActive: boolean; mustChangePassword: boolean }
type CreateTenantAdministratorResult = { user: TenantAdministrator; temporaryPassword: string }
type OrganizationAudit = { id: string; action: string; actorName: string; targetUserName: string | null; targetUserEmail: string | null; occurredAtUtc: string }

export function PlatformRoutes({ path, go, logout, pending, changeLocale }: { path: string; go: (path: string) => void; logout: () => Promise<void>; pending: boolean; changeLocale: (locale: SupportedLocale) => Promise<boolean> }) {
  const parts = path.slice('/plataforma/organizacoes'.length).split('/').filter(Boolean)
  return <PlatformShell logout={logout} pending={pending} go={go} changeLocale={changeLocale}>{parts[0] === 'nova' ? <OrganizationForm go={go} /> : parts[0] ? <OrganizationDetail id={parts[0]} go={go} /> : <OrganizationList go={go} />}</PlatformShell>
}

function PlatformShell({ children, go, logout, pending, changeLocale }: { children: ReactNode; go: (path: string) => void; logout: () => Promise<void>; pending: boolean; changeLocale: (locale: SupportedLocale) => Promise<boolean> }) {
  const { t } = useLocale(); const [settingsOpen, setSettingsOpen] = useState(false)
  return <><div className="application-shell"><aside className="sidebar"><div className="brand"><span className="brand-mark" aria-hidden="true">V</span><span>Vivat Flow</span></div><nav aria-label={t('navigation.platform')}><button className="nav-link active-nav" onClick={() => go('/plataforma/organizacoes')}>{t('platform.organizations')}</button></nav><div className="account-area platform-account-area"><strong>{t('platform.controlPlane')}</strong><button className="settings-button" type="button" onClick={() => setSettingsOpen(true)}>⚙ {t('settings.title')}</button><button className="logout-button" disabled={pending} onClick={() => void logout()}>{t('auth.logout')}</button></div></aside><main className="app-content">{children}</main></div>{settingsOpen && <UserSettingsModal close={() => setSettingsOpen(false)} changeLocale={changeLocale} />}</>
}

function OrganizationList({ go }: { go: (path: string) => void }) {
  const { t, locale } = useLocale(); const [items, setItems] = useState<Organization[]>([]), [summary, setSummary] = useState<Summary>(), [loading, setLoading] = useState(true), [error, setError] = useState('')
  const message = (caught: unknown, fallback: string) => caught instanceof ApiError ? (Object.values(caught.errors).flat()[0] ?? caught.message ?? fallback) : fallback
  const load = useCallback(async () => { try { const [organizations, dashboard] = await Promise.all([request('/api/platform/organizations'), request('/api/platform/organizations/summary')]); if (!organizations.ok) throw await ApiError.from(organizations); if (!dashboard.ok) throw await ApiError.from(dashboard); setItems(await organizations.json() as Organization[]); setSummary(await dashboard.json() as Summary) } catch (caught) { setError(message(caught, t('platform.loadFailure'))) } finally { setLoading(false) } }, [t])
  useEffect(() => { void Promise.resolve().then(load) }, [load])
  const date = (value: string) => new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
  return <><div className="page-heading"><div><p className="eyebrow">{t('platform.controlPlane')}</p><h2>{t('platform.listTitle')}</h2><p>{t('platform.listSubtitle')}</p></div><button onClick={() => go('/plataforma/organizacoes/nova')}>{t('platform.newOrganization')}</button></div>{summary && <section className="quote-kpis"><article><span>{t('platform.total')}</span><strong>{summary.total}</strong></article><article><span>{t('platform.active')}</span><strong>{summary.active}</strong></article><article><span>{t('platform.suspended')}</span><strong>{summary.suspended}</strong></article><article><span>{t('platform.deactivated')}</span><strong>{summary.deactivated}</strong></article></section>}<section className="card"><div className="section-heading"><div><h3>{t('platform.registered')}</h3><p>{t('platform.registeredHint')}</p></div></div>{error && <p className="error" role="alert">{error} <button className="secondary" onClick={() => { setError(''); setLoading(true); void load() }}>{t('common.retry')}</button></p>}{loading ? <LoadingState size="sm" /> : items.length === 0 ? <div className="empty-state"><h3>{t('platform.emptyTitle')}</h3><p>{t('platform.emptyHint')}</p></div> : <div className="table-wrap"><table><thead><tr><th>{t('platform.organization')}</th><th>{t('platform.identifier')}</th><th>{t('common.status')}</th><th>{t('common.createdAt')}</th><th>{t('common.actions')}</th></tr></thead><tbody>{items.map(item => <tr key={item.id}><td>{item.name}</td><td>{item.slug}</td><td><StatusBadge status={item.status} /></td><td>{date(item.createdAtUtc)}</td><td><button className="secondary" onClick={() => go(`/plataforma/organizacoes/${item.id}`)}>{t('platform.viewDetails')}</button></td></tr>)}</tbody></table></div>}</section></>
}

function OrganizationForm({ organization, go, saved }: { organization?: Organization; go: (path: string) => void; saved?: (organization: Organization) => void }) {
  const { t } = useLocale(); const [saving, setSaving] = useState(false), [error, setError] = useState(''), [errors, setErrors] = useState<Errors>({})
  const submit = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); if (saving) return; setSaving(true); setError(''); setErrors({}); try { const form = new FormData(event.currentTarget); const response = await request(organization ? `/api/platform/organizations/${organization.id}` : '/api/platform/organizations', { method: organization ? 'PUT' : 'POST', body: JSON.stringify({ name: form.get('name'), slug: form.get('slug') }) }); if (!response.ok) throw await ApiError.from(response); const value = await response.json() as Organization; if (saved) saved(value); else go(`/plataforma/organizacoes/${value.id}`) } catch (caught) { if (caught instanceof ApiError) setErrors(caught.errors); setError(caught instanceof ApiError ? Object.values(caught.errors).flat()[0] ?? caught.message ?? t('platform.saveFailure') : t('platform.saveFailure')) } finally { setSaving(false) } }
  return <><div className="page-heading"><div><p className="eyebrow">{t('platform.controlPlane')}</p><h2>{organization ? t('platform.editTitle') : t('platform.createTitle')}</h2><p>{t('platform.formHint')}</p></div></div><section className="card"><form onSubmit={submit}><label>{t('platform.organizationName')} *<input name="name" defaultValue={organization?.name} maxLength={200} required disabled={saving} />{errors.name && <small className="error" role="alert">{errors.name[0]}</small>}</label><label>{t('platform.identifier')} *<input name="slug" defaultValue={organization?.slug} maxLength={80} pattern="[a-z0-9]+(-[a-z0-9]+)*" placeholder="empresa-exemplo" required disabled={saving} />{errors.slug && <small className="error" role="alert">{errors.slug[0]}</small>}</label>{error && !Object.keys(errors).length && <p className="error" role="alert">{error}</p>}<div className="actions"><button className="secondary" type="button" disabled={saving} onClick={() => go(organization ? `/plataforma/organizacoes/${organization.id}` : '/plataforma/organizacoes')}>{t('common.cancel')}</button><button disabled={saving}>{saving ? t('platform.saving') : t('platform.saveOrganization')}</button></div></form></section></>
}

function OrganizationDetail({ id, go }: { id: string; go: (path: string) => void }) {
  const { t, locale } = useLocale(); const [organization, setOrganization] = useState<Organization | null>(null), [loading, setLoading] = useState(true), [error, setError] = useState(''), [editing, setEditing] = useState(false), [action, setAction] = useState<'suspend' | 'deactivate' | 'activate' | null>(null), [saving, setSaving] = useState(false), [auditRevision, setAuditRevision] = useState(0)
  const load = useCallback(async () => { try { const response = await request(`/api/platform/organizations/${id}`); if (!response.ok) throw await ApiError.from(response); setOrganization(await response.json() as Organization) } catch (caught) { setError(caught instanceof ApiError ? caught.message || t('platform.loadOneFailure') : t('platform.loadOneFailure')) } finally { setLoading(false) } }, [id, t])
  useEffect(() => { void Promise.resolve().then(load) }, [load])
  const transition = async () => { if (!organization || !action || saving) return; setSaving(true); setError(''); try { const response = await request(`/api/platform/organizations/${organization.id}/${action}`, { method: 'POST' }); if (!response.ok) throw await ApiError.from(response); setOrganization(await response.json() as Organization); setAction(null); setAuditRevision(revision => revision + 1) } catch (caught) { setError(caught instanceof ApiError ? caught.message || t('platform.updateFailure') : t('platform.updateFailure')) } finally { setSaving(false) } }
  const date = (value: string) => new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
  if (loading) return <LoadingState size="lg" />
  if (!organization) return <section className="card"><h2>{t('platform.missingTitle')}</h2><p className="error" role="alert">{error || t('platform.missingHint')}</p><button onClick={() => go('/plataforma/organizacoes')}>{t('platform.backToOrganizations')}</button></section>
  if (editing) return <OrganizationForm organization={organization} go={go} saved={value => { setOrganization(value); setEditing(false); setAuditRevision(revision => revision + 1) }} />
  return <><div className="page-heading"><div><p className="eyebrow">{t('platform.controlPlane')}</p><h2>{organization.name}</h2><p>{t('platform.identifier')}: {organization.slug}</p></div></div>{error && <p className="error" role="alert">{error}</p>}<section className="card"><dl className="service-detail-grid"><Info label={t('platform.name')} value={organization.name}/><Info label={t('platform.identifier')} value={organization.slug}/><Info label={t('common.status')} value={<StatusBadge status={organization.status} />}/><Info label={t('common.createdAt')} value={date(organization.createdAtUtc)}/><Info label={t('common.updatedAt')} value={date(organization.updatedAtUtc)}/></dl><div className="actions"><button className="secondary" disabled={saving} onClick={() => setEditing(true)}>{t('platform.editBasic')}</button>{organization.status === 'Active' && <button className="secondary" disabled={saving} onClick={() => setAction('suspend')}>{t('platform.suspend')}</button>}{organization.status === 'Suspended' && <button disabled={saving} onClick={() => setAction('activate')}>{t('platform.activate')}</button>}{organization.status !== 'Deactivated' && <button className="secondary" disabled={saving} onClick={() => setAction('deactivate')}>{t('platform.deactivate')}</button>}</div></section><OrganizationServiceLines organizationId={organization.id}/><OrganizationAdministrators organizationId={organization.id} status={organization.status} onCreated={() => setAuditRevision(revision => revision + 1)}/><OrganizationAudit organizationId={organization.id} refreshKey={auditRevision}/>{action && <Confirmation action={action} pending={saving} cancel={() => setAction(null)} confirm={() => void transition()}/>}</>
}

function OrganizationServiceLines({ organizationId }: { organizationId: string }) {
  const [lines, setLines] = useState<OrganizationServiceLine[] | null>(null), [error, setError] = useState(''), [notice, setNotice] = useState(''), [pendingLineId, setPendingLineId] = useState<string | null>(null), [disableTarget, setDisableTarget] = useState<OrganizationServiceLine | null>(null)
  const load = useCallback(async () => { setError(''); try { const response = await request(`/api/platform/organizations/${organizationId}/service-lines`); if (!response.ok) throw await ApiError.from(response); setLines(await response.json() as OrganizationServiceLine[]) } catch { setError('Não foi possível carregar as linhas de serviço desta empresa.') } }, [organizationId])
  useEffect(() => { void Promise.resolve().then(load) }, [load])
  const change = async (line: OrganizationServiceLine, enabled: boolean) => { if (pendingLineId) return; setPendingLineId(line.id); setError(''); setNotice(''); try { const response = await request(`/api/platform/organizations/${organizationId}/service-lines/${line.id}`, { method: enabled ? 'POST' : 'DELETE', body: '{}' }); if (!response.ok) throw await ApiError.from(response); setLines(current => current?.map(item => item.id === line.id ? { ...item, isEnabled: enabled } : item) ?? null); setNotice(enabled ? 'Linha de serviço habilitada para esta empresa.' : 'Linha de serviço desabilitada. Os serviços existentes foram preservados.') } catch { setError('Não foi possível atualizar as linhas de serviço desta empresa.') } finally { setPendingLineId(null); setDisableTarget(null) } }
  return <section className="card section-card"><div className="section-heading"><div><h3>Linhas de serviço</h3><p>Defina quais linhas esta empresa pode usar em novos serviços.</p></div></div>{notice && <p className="notice" role="status">{notice}</p>}{error && <div className="error-panel" role="alert"><p>{error}</p><button className="secondary" disabled={pendingLineId !== null} onClick={() => void load()}>Tentar novamente</button></div>}{lines === null ? <LoadingState size="sm" /> : lines.length === 0 ? <div className="empty-state compact"><h4>Nenhuma linha de serviço disponível</h4><p>Não há linhas de serviço cadastradas na plataforma.</p></div> : <div className="table-wrap"><table><thead><tr><th>Linha de serviço</th><th>Disponibilidade</th><th>Uso nesta empresa</th></tr></thead><tbody>{lines.map(line => <tr key={line.id}><td><strong>{line.name}</strong></td><td>{line.isActive ? <span className="status active">Disponível</span> : <span className="status inactive">Indisponível na plataforma</span>}</td><td>{line.isActive ? <label className="service-line-toggle"><input type="checkbox" role="switch" checked={line.isEnabled} disabled={pendingLineId !== null} aria-label={`Habilitar ${line.name} para esta empresa`} onChange={() => line.isEnabled ? setDisableTarget(line) : void change(line, true)} /><span className="service-line-toggle-track" aria-hidden="true"/><span>{pendingLineId === line.id ? 'Atualizando...' : line.isEnabled ? 'Habilitada' : 'Desabilitada'}</span></label> : <span className="field-hint">Indisponível para novos cadastros</span>}</td></tr>)}</tbody></table></div>}{disableTarget && <ServiceLineDisableConfirmation line={disableTarget} pending={pendingLineId !== null} cancel={() => setDisableTarget(null)} confirm={() => void change(disableTarget, false)} />}</section>
}

function OrganizationAdministrators({ organizationId, status, onCreated }: { organizationId: string; status: Status; onCreated: () => void }) {
  const { t } = useLocale()
  const [administrators, setAdministrators] = useState<TenantAdministrator[] | null>(null), [loading, setLoading] = useState(true), [error, setError] = useState(''), [formOpen, setFormOpen] = useState(false), [pending, setPending] = useState(false), [notice, setNotice] = useState(''), [fieldErrors, setFieldErrors] = useState<Errors>({}), [temporaryPassword, setTemporaryPassword] = useState(''), [passwordSaved, setPasswordSaved] = useState(false), [passwordError, setPasswordError] = useState('')
  const load = useCallback(async () => {
    setError(''); setLoading(true)
    try {
      const response = await request(`/api/platform/organizations/${organizationId}/administrators`)
      if (!response.ok) throw await ApiError.from(response)
      setAdministrators(await response.json() as TenantAdministrator[])
    } catch (caught) {
      setError(caught instanceof ApiError ? (Object.values(caught.errors).flat()[0] ?? caught.message) || t('platform.administratorLoadFailure') : t('platform.administratorLoadFailure'))
    } finally { setLoading(false) }
  }, [organizationId, t])
  useEffect(() => { void Promise.resolve().then(load) }, [load])

  const create = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (pending) return
    const formElement = event.currentTarget
    setPending(true); setError(''); setNotice(''); setFieldErrors({})
    try {
      const form = new FormData(formElement)
      const response = await request(`/api/platform/organizations/${organizationId}/administrators`, { method: 'POST', body: JSON.stringify({ fullName: form.get('fullName'), email: form.get('email') }) })
      if (!response.ok) throw await ApiError.from(response)
      const result = await response.json() as CreateTenantAdministratorResult
      setTemporaryPassword(result.temporaryPassword); setPasswordSaved(false); setPasswordError(''); setFormOpen(false)
      setNotice(t('platform.administratorCreated', { name: result.user.fullName }))
      await load(); onCreated()
    } catch (caught) {
      if (caught instanceof ApiError) setFieldErrors(caught.errors)
      setError(caught instanceof ApiError ? (Object.values(caught.errors).flat()[0] ?? caught.message) || t('platform.administratorCreateFailure') : t('platform.administratorCreateFailure'))
    } finally { setPending(false) }
  }

  const active = status === 'Active'
  return <>
    <section className="card section-card">
      <div className="section-heading"><div><h3>{t('platform.administrators')}</h3><p>{t('platform.administratorsHint')}</p></div><button disabled={!active} onClick={() => { setFieldErrors({}); setError(''); setFormOpen(true) }}>{t('platform.addAdministrator')}</button></div>
      {!active && <p className="field-hint" role="status">{t(status === 'Suspended' ? 'platform.suspendedProvisioning' : 'platform.deactivatedProvisioning')}</p>}
      {notice && <p className="notice" role="status">{notice}</p>}
      {error && !formOpen && <div className="error-panel" role="alert"><p>{error}</p><button className="secondary" disabled={pending} onClick={() => void load()}>{t('common.retry')}</button></div>}
      {loading ? <LoadingState size="sm" /> : administrators?.length === 0 ? <div className="empty-state compact"><h4>{t('platform.noAdministrators')}</h4><p>{t('platform.noAdministratorsHint')}</p></div> : administrators && <>
        <div className="table-wrap platform-admin-table"><table><thead><tr><th>{t('platform.administratorName')}</th><th>{t('platform.administratorEmail')}</th><th>{t('common.status')}</th></tr></thead><tbody>{administrators.map(item => <tr key={item.id}><td>{item.fullName}</td><td>{item.email}</td><td><span className={`status ${item.isActive ? 'active' : 'inactive'}`}>{t(item.isActive ? 'platform.administratorActive' : 'platform.administratorInactive')}</span>{item.mustChangePassword && <small className="platform-admin-password-hint">{t('platform.passwordChangeRequired')}</small>}</td></tr>)}</tbody></table></div>
        <div className="platform-admin-cards">{administrators.map(item => <article className="mobile-record" key={item.id}><div className="mobile-record-heading"><h4>{item.fullName}</h4><span className={`status ${item.isActive ? 'active' : 'inactive'}`}>{t(item.isActive ? 'platform.administratorActive' : 'platform.administratorInactive')}</span></div><dl><dt>{t('platform.administratorEmail')}</dt><dd>{item.email}</dd>{item.mustChangePassword && <><dt>{t('platform.password')}</dt><dd>{t('platform.passwordChangeRequired')}</dd></>}</dl></article>)}</div>
      </>}
    </section>
    {formOpen && <div className="modal-backdrop"><section className="card modal" role="dialog" aria-modal="true" aria-labelledby="tenant-admin-form-title"><h2 id="tenant-admin-form-title">{t('platform.addAdministrator')}</h2><p>{t('platform.temporaryPasswordHint')}</p><form onSubmit={create}><label>{t('platform.administratorName')} *<input name="fullName" maxLength={120} required disabled={pending}/>{fieldErrors.fullName && <small className="error" role="alert">{fieldErrors.fullName[0]}</small>}</label><label>{t('platform.administratorEmail')} *<input name="email" type="email" maxLength={254} required disabled={pending}/>{fieldErrors.email && <small className="error" role="alert">{fieldErrors.email[0]}</small>}</label>{error && !Object.keys(fieldErrors).length && <p className="error" role="alert">{error}</p>}<div className="actions"><button className="secondary" type="button" disabled={pending} onClick={() => setFormOpen(false)}>{t('common.cancel')}</button><button disabled={pending}>{pending ? t('platform.creatingAdministrator') : t('platform.createAdministrator')}</button></div></form></section></div>}
    {temporaryPassword && <div className="modal-backdrop"><section className="card modal" role="dialog" aria-modal="true" aria-labelledby="temporary-admin-password-title"><h2 id="temporary-admin-password-title">{t('platform.temporaryPasswordTitle')}</h2><p>{t('platform.temporaryPasswordOneTime')}</p><p className="temporary-password-value"><code>{temporaryPassword}</code></p><p>{t('platform.temporaryPasswordMustChange')}</p>{passwordError && <p className="error" role="alert">{passwordError}</p>}<button className="secondary" onClick={() => void navigator.clipboard.writeText(temporaryPassword).then(() => { setPasswordSaved(true); setPasswordError('') }).catch(() => setPasswordError(t('platform.passwordCopyFailure')))}>{t('platform.copyPassword')}</button>{passwordSaved && <p role="status">{t('platform.passwordSaved')}</p>}<label className="password-saved-confirmation"><input type="checkbox" checked={passwordSaved} onChange={event => setPasswordSaved(event.target.checked)}/>{t('platform.passwordSavedConfirm')}</label><div className="actions"><button disabled={!passwordSaved} onClick={() => { setTemporaryPassword(''); setPasswordSaved(false) }}>{t('platform.finishProvisioning')}</button></div></section></div>}
  </>
}

function OrganizationAudit({ organizationId, refreshKey }: { organizationId: string; refreshKey: number }) {
  const { t, locale } = useLocale(); const [history, setHistory] = useState<OrganizationAudit[] | null>(null), [error, setError] = useState('')
  const load = useCallback(async () => {
    setError('')
    try {
      const response = await request(`/api/platform/organizations/${organizationId}/audit`)
      if (!response.ok) throw await ApiError.from(response)
      setHistory(await response.json() as OrganizationAudit[])
    } catch (caught) { setError(caught instanceof ApiError ? caught.message || t('platform.auditLoadFailure') : t('platform.auditLoadFailure')) }
  }, [organizationId, t])
  useEffect(() => { void Promise.resolve().then(load) }, [load, refreshKey])
  const date = (value: string) => new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
  const description = (entry: OrganizationAudit) => entry.action === 'TENANT_ADMINISTRATOR_CREATED'
    ? t('platform.auditAdministratorCreated', { email: entry.targetUserEmail ?? entry.targetUserName ?? '' })
    : t(`platform.auditActions.${entry.action}`)
  return <section className="card section-card platform-audit"><div className="section-heading"><div><h3>{t('platform.administrativeHistory')}</h3><p>{t('platform.administrativeHistoryHint')}</p></div></div>{error && <div className="error-panel" role="alert"><p>{error}</p><button className="secondary" onClick={() => void load()}>{t('common.retry')}</button></div>}{history === null && !error ? <LoadingState size="sm" /> : history === null ? null : history.length === 0 ? <div className="empty-state compact"><p>{t('platform.noAdministrativeEvents')}</p></div> : <ol className="platform-audit-list">{history.map(entry => <li key={entry.id}><time dateTime={entry.occurredAtUtc}>{date(entry.occurredAtUtc)}</time><p><strong>{entry.actorName}</strong> {description(entry)}</p></li>)}</ol>}</section>
}


function ServiceLineDisableConfirmation({ line, pending, cancel, confirm }: { line: OrganizationServiceLine; pending: boolean; cancel: () => void; confirm: () => void }) { return <div className="modal-backdrop"><section className="card modal" role="dialog" aria-modal="true" aria-labelledby="disable-service-line-title"><h2 id="disable-service-line-title">Desabilitar linha de serviço?</h2><p>Os serviços existentes em <strong>{line.name}</strong> serão preservados, mas essa linha não poderá ser usada em novos cadastros enquanto estiver desabilitada.</p><div className="actions"><button className="secondary" disabled={pending} onClick={cancel}>Cancelar</button><button disabled={pending} onClick={confirm}>{pending ? 'Desabilitando...' : 'Desabilitar'}</button></div></section></div> }
function Info({ label, value }: { label: string; value: ReactNode }) { return <div className="service-detail-item"><dt>{label}</dt><dd>{value}</dd></div> }
function StatusBadge({ status }: { status: Status }) { const { t } = useLocale(); return <span className={`status ${status === 'Active' ? 'active' : 'inactive'}`}>{t(`platform.${status.toLowerCase()}Status`)}</span> }
function Confirmation({ action, pending, cancel, confirm }: { action: 'suspend' | 'deactivate' | 'activate'; pending: boolean; cancel: () => void; confirm: () => void }) { const { t } = useLocale(); const key = action === 'suspend' ? 'Suspend' : action === 'deactivate' ? 'Deactivate' : 'Activate'; return <div className="modal-backdrop"><section className="card modal" role="dialog" aria-modal="true" aria-label={t(`platform.confirm${key}Title`)}><h2>{t(`platform.confirm${key}Title`)}</h2><p>{t(`platform.confirm${key}Text`)}</p><div className="actions"><button className="secondary" disabled={pending} onClick={cancel}>{t('common.cancel')}</button><button disabled={pending} onClick={confirm}>{pending ? t('common.processing') : t('common.save')}</button></div></section></div> }
