export type CustomerSummary = {
  id: string; legalName: string; tradeName: string | null; cnpj: string; isActive: boolean; isComplete: boolean; missingRequiredFields: string[]
  createdAtUtc: string; updatedAtUtc: string
}

export type Contact = {
  id: string; customerId: string; name: string; roleOrDepartment: string | null; email: string | null; phone: string | null
  isPrimary: boolean; isActive: boolean; createdAtUtc: string; updatedAtUtc: string
}

export type Unit = {
  id: string; customerId: string; name: string; street: string; number: string; complement: string | null; district: string | null
  city: string; stateCode: string; postalCode: string | null; isPrimary: boolean; isActive: boolean; createdAtUtc: string; updatedAtUtc: string
}

export type Customer = CustomerSummary & {
  notes: string | null; createdByUserId: string; updatedByUserId: string; version: string; contacts: Contact[]; units: Unit[]
}

export type CustomerList = { items: CustomerSummary[]; page: number; pageSize: number; totalCount: number }
export type CustomerInput = { legalName: string; tradeName: string; cnpj: string; notes: string }
export type ContactInput = { name: string; roleOrDepartment: string; email: string; phone: string; isPrimary: boolean }
export type UnitInput = { name: string; street: string; number: string; complement: string; district: string; city: string; stateCode: string; postalCode: string; isPrimary: boolean }
