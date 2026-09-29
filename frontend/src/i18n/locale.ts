export const supportedLocales = ['pt-BR', 'en-US'] as const
export type SupportedLocale = typeof supportedLocales[number]
export const fallbackLocale: SupportedLocale = 'en-US'
export const localeStorageKey = 'vivatflow.locale'

export function normalizeLocale(value?: string | null): SupportedLocale | null {
  const language = value?.trim().toLowerCase()
  if (!language) return null
  if (language === 'pt' || language.startsWith('pt-')) return 'pt-BR'
  if (language === 'en' || language.startsWith('en-')) return 'en-US'
  return null
}

export function resolveLocale(saved?: string | null, local?: string | null, browserLanguages: readonly string[] = []): SupportedLocale {
  return normalizeLocale(saved) ?? normalizeLocale(local) ?? browserLanguages.map(normalizeLocale).find((locale): locale is SupportedLocale => locale !== null) ?? fallbackLocale
}

export function browserLocale(): SupportedLocale {
  const languages = typeof navigator === 'undefined' ? [] : [...(navigator.languages ?? []), navigator.language]
  return resolveLocale(null, null, languages)
}
