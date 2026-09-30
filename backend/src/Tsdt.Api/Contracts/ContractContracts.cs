using Tsdt.Api.Quotes;

namespace Tsdt.Api.Contracts;

public sealed record CreateContractFromQuoteRequest(Guid QuoteId, DateOnly? StartDate, DateOnly? EndDate, string? PaymentTerms, string? Notes);
public sealed record UpdateContractDraftRequest(DateOnly? StartDate, DateOnly? EndDate, string? PaymentTerms, string? Notes, Guid ExpectedVersion);
public sealed record ContractVersionRequest(Guid ExpectedVersion);
public sealed record ContractItemResponse(Guid Id, Guid QuoteItemId, Guid ServiceId, string ServiceCodeSnapshot, string ServiceNameSnapshot, Guid ServiceLineId, string ServiceLineCodeSnapshot, string ServiceLineNameSnapshot, int DisplayOrder);
public sealed record ContractSummaryResponse(Guid Id, Guid QuoteId, Guid CustomerId, string CustomerLegalNameSnapshot, ContractStatus Status, decimal ApprovedTotalAmount, DateOnly? StartDate, DateOnly? EndDate, DateTimeOffset UpdatedAtUtc);
public sealed record ContractDetailResponse(Guid Id, Guid QuoteId, Guid CustomerId, string CustomerLegalNameSnapshot, ContractStatus Status, decimal ApprovedTotalAmount, QuotePaymentType PaymentType, int? InstallmentCount, DateOnly? StartDate, DateOnly? EndDate, string? PaymentTerms, string? Notes, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, Guid Version, IReadOnlyList<ContractItemResponse> Items);
public sealed record ContractListResponse(IReadOnlyList<ContractSummaryResponse> Items, int Page, int PageSize, int TotalCount);
