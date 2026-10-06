export function completionActions(errors: Record<string, string[]>): { customer: boolean; quote: boolean } {
  const keys = Object.keys(errors)
  const customerKeys = new Set(['contact', 'unit'])
  return {
    customer: keys.some(key => customerKeys.has(key)),
    quote: keys.some(key => !customerKeys.has(key)),
  }
}
