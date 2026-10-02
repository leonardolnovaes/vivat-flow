namespace Tsdt.Api.Documents;

public enum DocumentCategory { General, Report, Certificate, Contract, Evidence, Photo, SignedDocument, Other }
public enum DocumentContextType { Customer, WorkOrder, Contract, Quote, CustomerUnit }
public enum DocumentPurpose { InternalSupporting, CustomerDeliverable }
public enum DocumentAccessLevel { User, Manager, Admin }

public sealed class DocumentRecord
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CustomerId { get; set; }
    public required string OriginalFileName { get; set; }
    public Guid StorageKey { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DocumentCategory Category { get; set; }
    public DocumentPurpose Purpose { get; set; }
    public string? Description { get; set; }
    public DocumentContextType? ContextType { get; set; }
    public Guid? ContextId { get; set; }
    public DateTimeOffset UploadedAtUtc { get; set; }
    public required string UploadedByUserId { get; set; }
}

public sealed class DocumentAuditRecord
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CustomerId { get; set; }
    public required string ActorUserId { get; set; }
    public required string Action { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DocumentContextType? ContextType { get; set; }
    public Guid? ContextId { get; set; }
}
