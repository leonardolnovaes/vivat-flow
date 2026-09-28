type LoadingStateProps = {
  message?: string
  size?: 'sm' | 'md' | 'lg'
}

export function LoadingState({ message = 'Carregando...', size = 'md' }: LoadingStateProps) {
  return <div className={`loading-state loading-state-${size}`} role="status" aria-live="polite">
    <span className="loading-spinner" aria-hidden="true" />
    <span className="loading-state-message">{message}</span>
  </div>
}
