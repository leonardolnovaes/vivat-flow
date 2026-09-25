namespace Tsdt.Api.Customers;

public sealed class Customer
{
    public Guid Id { get; set; }
    public required string LegalName { get; set; }
    public string? TradeName { get; set; }
    public required string Cnpj { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public required string CreatedByUserId { get; set; }
    public required string UpdatedByUserId { get; set; }
    public Guid Version { get; set; }
    public List<CustomerContact> Contacts { get; set; } = [];
    public List<CustomerUnit> Units { get; set; } = [];
}

public sealed class CustomerContact
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public required string Name { get; set; }
    public string? RoleOrDepartment { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Customer Customer { get; set; } = null!;
}

public sealed class CustomerUnit
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public required string Name { get; set; }
    public required string Street { get; set; }
    public required string Number { get; set; }
    public string? Complement { get; set; }
    public string? District { get; set; }
    public required string City { get; set; }
    public required string StateCode { get; set; }
    public string? PostalCode { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Customer Customer { get; set; } = null!;
}

public sealed class CustomerAuditRecord
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public required string ActorUserId { get; set; }
    public required string Action { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string? ChangedFields { get; set; }
    public Customer Customer { get; set; } = null!;
}
