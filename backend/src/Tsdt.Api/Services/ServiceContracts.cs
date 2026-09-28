namespace Tsdt.Api.Services;

public sealed record CreateServiceRequest(string? Code, string? Name, string? Description, decimal? BasePrice);
public sealed record UpdateServiceRequest(string? Code, string? Name, string? Description, decimal? BasePrice, Guid ExpectedVersion);
public sealed record ServiceVersionRequest(Guid ExpectedVersion);

public sealed record ServiceListResponse(IReadOnlyList<ServiceSummaryResponse> Items, int Page, int PageSize, int TotalCount);
public sealed record ServiceSummaryResponse(Guid Id, string Code, string Name, decimal? BasePrice, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record ServiceDetailResponse(Guid Id, string Code, string Name, string? Description, decimal? BasePrice, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string CreatedByUserId, string UpdatedByUserId, Guid Version);
