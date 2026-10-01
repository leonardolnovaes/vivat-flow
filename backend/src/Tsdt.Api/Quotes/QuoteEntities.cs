using Tsdt.Api.Customers;
using Tsdt.Api.Identity;
using Tsdt.Api.Services;

namespace Tsdt.Api.Quotes;

public enum QuoteStatus { Draft, AwaitingApproval, ChangesRequested, Approved, Rejected, Expired, Cancelled }
public enum QuoteListScope { Open, All, Closed }
public enum QuoteClientResponseType { Approved, ChangesRequested, Rejected }
public enum QuotePaymentType { Cash, Installments }
public enum QuoteRiskDegree { One = 1, Two = 2, Three = 3, Four = 4 }

public sealed class Quote
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Number { get; set; }
    public Guid CustomerId { get; set; }
    public required string CustomerLegalNameSnapshot { get; set; }
    public required string CustomerCnpjSnapshot { get; set; }
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;
    public decimal? TotalAmount { get; set; }
    public QuotePaymentType? PaymentType { get; set; }
    public int? InstallmentCount { get; set; }
    public int? EmployeeCount { get; set; }
    public QuoteRiskDegree? RiskDegree { get; set; }
    public Guid? ServiceUnitId { get; set; }
    public string? ServiceAddressSnapshot { get; set; }
    public string? Notes { get; set; }
    public string? ResponsibleUserId { get; set; }
    public DateTimeOffset? SentForApprovalAt { get; set; }
    public string? ApprovalRecipientName { get; set; }
    public string? ApprovalRecipientEmail { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public DateTimeOffset? ClientResponseAt { get; set; }
    public QuoteClientResponseType? ClientResponseType { get; set; }
    public string? ClientResponseNotes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public required string CreatedByUserId { get; set; }
    public required string UpdatedByUserId { get; set; }
    public Guid Version { get; set; }
    public Customer Customer { get; set; } = null!;
    public ApplicationUser? ResponsibleUser { get; set; }
    public List<QuoteItem> Items { get; set; } = [];
    public List<QuoteVisit> Visits { get; set; } = [];
    public List<QuoteApprovalRecipient> ApprovalRecipients { get; set; } = [];
}
public sealed class QuoteApprovalRecipient
{
    public Guid QuoteId { get; set; }
    public Guid CustomerContactId { get; set; }
    public Quote Quote { get; set; } = null!;
    public CustomerContact Contact { get; set; } = null!;
}
public sealed class QuoteItem
{
    public Guid Id { get; set; }
    public Guid QuoteId { get; set; }
    public Guid ServiceId { get; set; }
    public required string ServiceCodeSnapshot { get; set; }
    public required string ServiceNameSnapshot { get; set; }
    public Guid ServiceLineIdSnapshot { get; set; }
    public required string ServiceLineCodeSnapshot { get; set; }
    public required string ServiceLineNameSnapshot { get; set; }
    public int DisplayOrder { get; set; }
    public Quote Quote { get; set; } = null!;
    public Service Service { get; set; } = null!;
}
public sealed class QuoteAuditRecord { public Guid Id { get; set; } public Guid QuoteId { get; set; } public required string ActorUserId { get; set; } public required string Action { get; set; } public DateTimeOffset OccurredAtUtc { get; set; } public string? ChangedFields { get; set; } public Quote Quote { get; set; } = null!; }
public enum QuoteVisitStatus { Scheduled, Completed, Cancelled }
public sealed class QuoteVisit { public Guid Id { get; set; } public Guid QuoteId { get; set; } public required string AssignedUserId { get; set; } public DateTimeOffset ScheduledStart { get; set; } public DateTimeOffset? ScheduledEnd { get; set; } public Guid? CustomerUnitId { get; set; } public string? LocationSnapshot { get; set; } public string? Notes { get; set; } public QuoteVisitStatus Status { get; set; } = QuoteVisitStatus.Scheduled; public DateTimeOffset CreatedAtUtc { get; set; } public required string CreatedByUserId { get; set; } public DateTimeOffset UpdatedAtUtc { get; set; } public required string UpdatedByUserId { get; set; } public Quote Quote { get; set; } = null!; public ApplicationUser AssignedUser { get; set; } = null!; }
public sealed class QuoteNumberCounter { public int Year { get; set; } public int LastNumber { get; set; } }
