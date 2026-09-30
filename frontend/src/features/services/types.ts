export type ServiceLine = { id: string; code: string; name: string }
export type ServiceSummary = { id: string; code: string; name: string; basePrice: number | null; serviceLineId: string; serviceLineCode: string; serviceLineName: string; isActive: boolean; createdAtUtc: string; updatedAtUtc: string }
export type Service = ServiceSummary & { description: string | null; createdByUserId: string; updatedByUserId: string; version: string }
export type ServiceList = { items: ServiceSummary[]; page: number; pageSize: number; totalCount: number }
export type ServiceInput = { code: string; name: string; description: string; basePrice: string; serviceLineId: string }
