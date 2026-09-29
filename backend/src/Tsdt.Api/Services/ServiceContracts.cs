namespace Tsdt.Api.Services;

public sealed record CreateServiceRequest(string? Code, string? Name, string? Description, decimal? BasePrice, Guid ServiceLineId = default);
public sealed record UpdateServiceRequest(string? Code, string? Name, string? Description, decimal? BasePrice, Guid ServiceLineId, Guid ExpectedVersion)
{
    public UpdateServiceRequest(string? code, string? name, string? description, decimal? basePrice, Guid expectedVersion) : this(code, name, description, basePrice, Guid.Empty, expectedVersion) { }
}
public sealed record ServiceVersionRequest(Guid ExpectedVersion);

public sealed record ServiceListResponse(IReadOnlyList<ServiceSummaryResponse> Items, int Page, int PageSize, int TotalCount);
public sealed record ServiceSummaryResponse(Guid Id, string Code, string Name, decimal? BasePrice, Guid ServiceLineId, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record ServiceDetailResponse(Guid Id, string Code, string Name, string? Description, decimal? BasePrice, Guid ServiceLineId, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string CreatedByUserId, string UpdatedByUserId, Guid Version);
