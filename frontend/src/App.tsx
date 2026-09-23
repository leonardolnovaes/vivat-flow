import { useCallback, useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import './App.css'
import { clearCsrfToken, request } from './api'

type CurrentUser = { id: string; fullName: string; email: string; roles: string[]; mustChangePassword: boolean }
type AdminUser = { id: string; fullName: string; email: string; roles: string[]; isActive: boolean; mustChangePassword: boolean }
type PasswordResult = { user: AdminUser; temporaryPassword: string }
const labels: Record<string, string> = { ADMIN: 'Administrador', MANAGER: 'Gestor', USER: 'Usuário' }

function App() {
  const [user, setUser] = useState<CurrentUser | null>(null); const [loading, setLoading] = useState(true); const [error, setError] = useState(''); const [users, setUsers] = useState<AdminUser[]>([]); const [adminError, setAdminError] = useState(''); const [temporaryPassword, setTemporaryPassword] = useState('')
  const loadSession = useCallback(async () => { const response = await request('/api/auth/me'); if (!response.ok) { setUser(null); clearCsrfToken(); setLoading(false); return }; setUser(await response.json() as CurrentUser); setLoading(false) }, [])
  const loadUsers = useCallback(async () => { const response = await request('/api/admin/users'); if (response.ok) setUsers(await response.json() as AdminUser[]); else setAdminError('Não foi possível carregar os usuários.') }, [])
  const isAdministrator = user?.roles.includes('ADMIN') ?? false
  useEffect(() => {
    let isCurrent = true
    void request('/api/auth/me').then(async response => {
      const session = response.ok ? await response.json() as CurrentUser : null
      if (!isCurrent) return
      if (!response.ok) clearCsrfToken()
      setUser(session)
      setLoading(false)
    })
    return () => { isCurrent = false }
  }, [])
  useEffect(() => {
    if (!isAdministrator || user?.mustChangePassword) return
    let isCurrent = true
    void request('/api/admin/users').then(async response => {
      const loadedUsers = response.ok ? await response.json() as AdminUser[] : null
      if (!isCurrent) return
      if (loadedUsers) setUsers(loadedUsers)
      else setAdminError('Não foi possível carregar os usuários.')
    })
    return () => { isCurrent = false }
  }, [isAdministrator, user?.mustChangePassword])
  async function login(event: FormEvent<HTMLFormElement>) { event.preventDefault(); setError(''); const form = new FormData(event.currentTarget); const response = await request('/api/auth/login', { method: 'POST', body: JSON.stringify({ email: form.get('email'), password: form.get('password') }) }); if (!response.ok) { setError('Não foi possível entrar. Verifique seu e-mail e senha.'); return }; clearCsrfToken(); await loadSession() }
  async function changePassword(event: FormEvent<HTMLFormElement>) { event.preventDefault(); setError(''); const form = new FormData(event.currentTarget); const password = String(form.get('newPassword') ?? ''); if (password !== String(form.get('confirmation') ?? '')) { setError('A confirmação da nova senha não corresponde.'); return }; const response = await request('/api/auth/change-password', { method: 'POST', body: JSON.stringify({ currentPassword: form.get('currentPassword'), newPassword: password }) }); if (!response.ok) { setError('Não foi possível alterar a senha. Verifique os dados informados.'); return }; await loadSession() }
  async function logout() { await request('/api/auth/logout', { method: 'POST' }); setUser(null); clearCsrfToken() }
  async function createUser(event: FormEvent<HTMLFormElement>) { event.preventDefault(); setAdminError(''); setTemporaryPassword(''); const form = new FormData(event.currentTarget); const response = await request('/api/admin/users', { method: 'POST', body: JSON.stringify({ fullName: form.get('fullName'), email: form.get('email'), role: form.get('role') }) }); if (!response.ok) { setAdminError('Não foi possível criar o usuário. Verifique os dados informados.'); return }; const result = await response.json() as PasswordResult; setTemporaryPassword(result.temporaryPassword); event.currentTarget.reset(); await loadUsers() }
  async function setActive(target: AdminUser, active: boolean) { if (!active && !confirm(`Deseja desativar ${target.fullName}?`)) return; const response = await request(`/api/admin/users/${target.id}/${active ? 'activate' : 'deactivate'}`, { method: 'POST' }); if (!response.ok) { setAdminError('A alteração não foi permitida.'); return }; await loadUsers() }
  async function changeRole(target: AdminUser) { const role = prompt('Informe ADMIN, MANAGER ou USER:', target.roles[0]); if (!role || !confirm(`Deseja alterar o perfil de ${target.fullName}?`)) return; const response = await request(`/api/admin/users/${target.id}/role`, { method: 'PUT', body: JSON.stringify({ role }) }); if (!response.ok) { setAdminError('A alteração de perfil não foi permitida.'); return }; await loadUsers() }
  async function resetPassword(target: AdminUser) { if (!confirm(`Gerar uma nova senha temporária para ${target.fullName}?`)) return; setTemporaryPassword(''); const response = await request(`/api/admin/users/${target.id}/reset-password`, { method: 'POST' }); if (!response.ok) { setAdminError('Não foi possível gerar a nova senha temporária.'); return }; const result = await response.json() as PasswordResult; setTemporaryPassword(result.temporaryPassword); await loadUsers() }
  if (loading) return <main className="auth-page"><p>Carregando...</p></main>
  if (!user) return <main className="auth-page"><section className="card"><h1>TSDT ERP</h1><h2>Entrar</h2><form onSubmit={login}><label>E-mail<input name="email" type="email" autoComplete="email" required /></label><label>Senha<input name="password" type="password" autoComplete="current-password" required /></label>{error && <p className="error" role="alert">{error}</p>}<button>Entrar</button></form></section></main>
  if (user.mustChangePassword) return <main className="auth-page"><section className="card"><h1>Alterar senha</h1><p>Para continuar, defina uma nova senha.</p><form onSubmit={changePassword}><label>Senha atual<input name="currentPassword" type="password" required /></label><label>Nova senha<input name="newPassword" type="password" required /></label><label>Confirmar nova senha<input name="confirmation" type="password" required /></label>{error && <p className="error" role="alert">{error}</p>}<button>Alterar senha</button></form><button className="secondary" onClick={logout}>Sair</button></section></main>
  if (!user.roles.includes('ADMIN')) return <main className="app-page"><header><div><h1>TSDT ERP</h1><p>{user.fullName} · {user.email}</p></div><button className="secondary" onClick={logout}>Sair</button></header><section className="card"><h2>Bem-vindo</h2><p>Você está autenticado.</p></section></main>
  return <main className="app-page"><header><div><h1>TSDT ERP</h1><p>{user.fullName} · {user.email}</p></div><button className="secondary" onClick={logout}>Sair</button></header><h2>Administração → Usuários</h2><div className="admin-grid"><section className="card"><h3>Criar usuário</h3><form onSubmit={createUser}><label>Nome completo<input name="fullName" required /></label><label>E-mail<input name="email" type="email" required /></label><label>Perfil<select name="role" defaultValue="USER"><option value="ADMIN">Administrador</option><option value="MANAGER">Gestor</option><option value="USER">Usuário</option></select></label><button>Criar usuário</button></form></section><section className="card"><h3>Usuários</h3>{adminError && <p className="error" role="alert">{adminError}</p>}<div className="table-wrap"><table><thead><tr><th>Nome</th><th>E-mail</th><th>Perfil</th><th>Status</th><th>Troca de senha pendente</th><th>Ações</th></tr></thead><tbody>{users.map(target => <tr key={target.id}><td>{target.fullName}</td><td>{target.email}</td><td>{labels[target.roles[0]] ?? target.roles[0]}</td><td>{target.isActive ? 'Ativo' : 'Inativo'}</td><td>{target.mustChangePassword ? 'Sim' : 'Não'}</td><td className="actions"><button className="secondary" onClick={() => void setActive(target, !target.isActive)}>{target.isActive ? 'Desativar' : 'Ativar'}</button><button className="secondary" onClick={() => void changeRole(target)}>Alterar perfil</button><button className="secondary" onClick={() => void resetPassword(target)}>Gerar nova senha temporária</button></td></tr>)}</tbody></table></div></section></div>{temporaryPassword && <section className="password-result"><strong>Usuário criado ou atualizado com sucesso.</strong><p>Senha temporária:</p><code>{temporaryPassword}</code><button onClick={() => void navigator.clipboard.writeText(temporaryPassword)}>Copiar senha</button><p>Esta senha será exibida apenas uma vez. Envie-a ao usuário por um canal seguro.</p><button className="secondary" onClick={() => setTemporaryPassword('')}>Fechar</button></section>}</main>
}
export default App
