using Tsdt.Api.Quotes;

namespace Tsdt.Api.Contracts;

public enum ContractStatus { Draft, Active, Ended, Cancelled }

public sealed class Contract
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid QuoteId { get; set; }
    public Guid CustomerId { get; set; }
    public required string CustomerLegalNameSnapshot { get; set; }
    public ContractStatus Status { get; set; } = ContractStatus.Draft;
    public decimal ApprovedTotalAmount { get; set; }
    public QuotePaymentType PaymentType { get; set; }
    public int? InstallmentCount { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? PaymentTerms { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public required string CreatedByUserId { get; set; }
    public required string UpdatedByUserId { get; set; }
    public Guid Version { get; set; }
    public List<ContractItem> Items { get; set; } = [];
}

public sealed class ContractItem
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public Guid QuoteItemId { get; set; }
    public Guid ServiceId { get; set; }
    public required string ServiceCodeSnapshot { get; set; }
    public required string ServiceNameSnapshot { get; set; }
    public Guid ServiceLineId { get; set; }
    public required string ServiceLineCodeSnapshot { get; set; }
    public required string ServiceLineNameSnapshot { get; set; }
    public int DisplayOrder { get; set; }
    public Contract Contract { get; set; } = null!;
}

public sealed class ContractAuditRecord
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public required string ActorUserId { get; set; }
    public required string Action { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string? ChangedFields { get; set; }
    public Contract Contract { get; set; } = null!;
}
