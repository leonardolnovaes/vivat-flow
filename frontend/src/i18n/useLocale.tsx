import { useTranslation } from 'react-i18next'
import { localeStorageKey, type SupportedLocale } from './locale'

export function useLocale() {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage as SupportedLocale
  const setLocale = async (next: SupportedLocale, explicit = true) => {
    if (explicit) localStorage.setItem(localeStorageKey, next)
    await i18n.changeLanguage(next)
  }
  return { t, locale, setLocale }
}
