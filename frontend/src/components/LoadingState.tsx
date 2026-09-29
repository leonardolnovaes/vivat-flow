type LoadingStateProps = {
  message?: string
  size?: 'sm' | 'md' | 'lg'
}

import { useLocale } from '../i18n/useLocale'

export function LoadingState({ message, size = 'md' }: LoadingStateProps) {
  const { t } = useLocale()
  return <div className={`loading-state loading-state-${size}`} role="status" aria-live="polite">
    <span className="loading-spinner" aria-hidden="true" />
    <span className="loading-state-message">{message ?? t('loading')}</span>
  </div>
}
