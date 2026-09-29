import { useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import type { SupportedLocale } from '../i18n/locale'
import { useLocale } from '../i18n/useLocale'

export function UserSettingsModal({ close, changeLocale }: { close: () => void; changeLocale?: (locale: SupportedLocale) => Promise<boolean> }) {
  const { t, locale, setLocale } = useLocale()
  const previousFocus = useRef<HTMLElement | null>(null)
  useEffect(() => { previousFocus.current = document.activeElement as HTMLElement; return () => previousFocus.current?.focus() }, [])
  useEffect(() => { const onKeyDown = (event: KeyboardEvent) => { if (event.key === 'Escape') close() }; addEventListener('keydown', onKeyDown); return () => removeEventListener('keydown', onKeyDown) }, [close])
  const selectLocale = async (next: SupportedLocale) => {
    if (!changeLocale) {
      await setLocale(next)
      window.dispatchEvent(new CustomEvent<SupportedLocale>('vivatflow-locale-selected', { detail: next }))
      return
    }
    if (await changeLocale(next)) await setLocale(next)
  }
  return createPortal(<div className="modal-backdrop"><section className="card modal user-settings-modal" role="dialog" aria-modal="true" aria-labelledby="user-settings-title"><div className="settings-modal-heading"><h2 id="user-settings-title">{t('settings.title')}</h2><button type="button" className="settings-close" aria-label={t('settings.close')} onClick={close}>×</button></div><h3>{t('settings.preferences')}</h3><label>{t('settings.language')}<select value={locale} onChange={event => void selectLocale(event.target.value as SupportedLocale)}><option value="pt-BR">Português (Brasil)</option><option value="en-US">English (US)</option></select></label><div className="actions settings-actions"><button type="button" className="secondary" onClick={close}>{t('settings.close')}</button></div></section></div>, document.body)
}
