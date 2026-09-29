import { useState } from 'react'
import type { SupportedLocale } from './locale'
import { useLocale } from './useLocale'

export function LanguageSelector({ onChange }: { onChange?: (locale: SupportedLocale) => Promise<boolean> | boolean }) {
  const { t, locale, setLocale } = useLocale()
  const [open, setOpen] = useState(false)
  const shortLocale = locale === 'pt-BR' ? 'PT' : 'EN'
  const change = async (next: SupportedLocale) => {
    if (!onChange) {
      await setLocale(next)
      window.dispatchEvent(new CustomEvent<SupportedLocale>('vivatflow-locale-selected', { detail: next }))
      setOpen(false)
      return
    }
    if (await onChange(next)) {
      await setLocale(next)
      setOpen(false)
    }
  }
  return <div className="language-selector"><button type="button" className="language-selector-trigger" aria-label={t('common.language')} aria-haspopup="listbox" aria-expanded={open} onClick={() => setOpen(value => !value)}>🌐 {shortLocale}</button>{open && <div className="language-selector-menu" role="listbox" aria-label={t('common.language')}><button type="button" role="option" aria-selected={locale === 'pt-BR'} onClick={() => void change('pt-BR')}>Português (Brasil)</button><button type="button" role="option" aria-selected={locale === 'en-US'} onClick={() => void change('en-US')}>English (US)</button></div>}</div>
}
