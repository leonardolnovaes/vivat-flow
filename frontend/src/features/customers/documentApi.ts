import { ApiError, request } from '../../api'

export type DocumentCategory = 'General' | 'Report' | 'Certificate' | 'Contract' | 'Evidence' | 'Photo' | 'SignedDocument' | 'Other'
export type DocumentPurpose = 'InternalSupporting' | 'CustomerDeliverable'
export type DocumentContextType = 'Customer' | 'CustomerUnit' | 'Quote' | 'Contract' | 'WorkOrder'
export type CustomerDocument = {
  id: string; fileName: string; sizeBytes: number; category: DocumentCategory; purpose: DocumentPurpose
  description: string | null; contextType: DocumentContextType | null; contextLabel: string | null
  uploadedAtUtc: string; uploadedByName: string | null
}
export type DocumentList = { items: CustomerDocument[]; page: number; pageSize: number; totalCount: number }
export type ContextOption = { type: DocumentContextType; id: string; label: string }

async function call<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await request(path, init)
  if (!response.ok) throw await ApiError.from(response)
  return response.json() as Promise<T>
}

export const listDocuments = (customerId: string, page: number) => call<DocumentList>(`/api/customers/${customerId}/documents?page=${page}&pageSize=25`)
export const listDocumentContexts = (customerId: string) => call<ContextOption[]>(`/api/customers/${customerId}/documents/contexts`)

export function uploadDocument(customerId: string, input: { file: File; category: DocumentCategory; purpose: DocumentPurpose; description: string; context: ContextOption | null }) {
  const body = new FormData()
  body.set('file', input.file)
  body.set('category', input.category)
  body.set('purpose', input.purpose)
  if (input.description.trim()) body.set('description', input.description.trim())
  if (input.context) { body.set('contextType', input.context.type); body.set('contextId', input.context.id) }
  return call<CustomerDocument>(`/api/customers/${customerId}/documents`, { method: 'POST', body })
}

export async function downloadDocument(document: CustomerDocument) {
  const response = await request(`/api/documents/${document.id}/download`)
  if (!response.ok) throw await ApiError.from(response)
  const url = URL.createObjectURL(await response.blob())
  try {
    const anchor = window.document.createElement('a')
    anchor.href = url
    anchor.download = document.fileName
    window.document.body.append(anchor)
    anchor.click()
    anchor.remove()
  } finally { window.setTimeout(() => URL.revokeObjectURL(url), 60000) }
}
