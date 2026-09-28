namespace Tsdt.Api.Customers;

public sealed record CreateCustomerRequest(string? LegalName, string? TradeName, string? Cnpj, string? Notes);
public sealed record UpdateCustomerRequest(string? LegalName, string? TradeName, string? Cnpj, string? Notes, Guid ExpectedVersion);
public sealed record CustomerVersionRequest(Guid ExpectedVersion);
public sealed record CreateContactRequest(string? Name, string? RoleOrDepartment, string? Email, string? Phone, bool IsPrimary, Guid ExpectedVersion);
public sealed record UpdateContactRequest(string? Name, string? RoleOrDepartment, string? Email, string? Phone, bool IsPrimary, Guid ExpectedVersion);
public sealed record CreateUnitRequest(string? Name, string? Street, string? Number, string? Complement, string? District, string? City, string? StateCode, string? PostalCode, bool IsPrimary, Guid ExpectedVersion);
public sealed record UpdateUnitRequest(string? Name, string? Street, string? Number, string? Complement, string? District, string? City, string? StateCode, string? PostalCode, bool IsPrimary, Guid ExpectedVersion);

public sealed record CustomerListResponse(IReadOnlyList<CustomerSummaryResponse> Items, int Page, int PageSize, int TotalCount);
public sealed record CustomerSummaryResponse(Guid Id, string LegalName, string? TradeName, string Cnpj, bool IsActive, bool IsComplete, IReadOnlyList<string> MissingRequiredFields, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CustomerDetailResponse(Guid Id, string LegalName, string? TradeName, string Cnpj, string? Notes, bool IsActive, bool IsComplete, IReadOnlyList<string> MissingRequiredFields, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string CreatedByUserId, string UpdatedByUserId, Guid Version, IReadOnlyList<CustomerContactResponse> Contacts, IReadOnlyList<CustomerUnitResponse> Units);
public sealed record CustomerContactResponse(Guid Id, Guid CustomerId, string Name, string? RoleOrDepartment, string? Email, string? Phone, bool IsPrimary, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CustomerUnitResponse(Guid Id, Guid CustomerId, string Name, string Street, string Number, string? Complement, string? District, string City, string StateCode, string? PostalCode, bool IsPrimary, bool IsActive, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
