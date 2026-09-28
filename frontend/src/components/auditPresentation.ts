export function formatChangedFields(changedFields: string, labels: Readonly<Record<string, string>>, unknownLabel: string): string {
  const fields = changedFields.split(',').map(field => field.trim()).filter(Boolean)
  const translated = fields.map(field => labels[field] ?? unknownField(field, unknownLabel))
  return translated.length ? `Campos alterados: ${joinPtBr(translated)}.` : ''
}

function unknownField(field: string, fallback: string): string {
  if (import.meta.env.DEV) console.warn(`Missing audit field label: ${field}`)
  return fallback
}

function joinPtBr(values: string[]): string {
  if (values.length === 1) return values[0]
  if (values.length === 2) return `${values[0]} e ${values[1]}`
  return `${values.slice(0, -1).join(', ')} e ${values.at(-1)}`
}
