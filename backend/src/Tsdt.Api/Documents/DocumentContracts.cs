namespace Tsdt.Api.Documents;

public sealed record DocumentResponse(Guid Id, Guid CustomerId, string FileName, string ContentType, long SizeBytes,
    DocumentCategory Category, DocumentPurpose Purpose, string? Description, DocumentContextType? ContextType, Guid? ContextId,
    DateTimeOffset UploadedAtUtc, string UploadedByUserId)
{
    public string? ContextLabel { get; init; }
    public string? UploadedByName { get; init; }
}

public sealed record DocumentListResponse(IReadOnlyList<DocumentResponse> Items, int Page, int PageSize, int TotalCount);
public sealed record DocumentContextOption(DocumentContextType Type, Guid Id, string Label);
public sealed record DocumentDownload(Stream Content, string ContentType, string FileName);
