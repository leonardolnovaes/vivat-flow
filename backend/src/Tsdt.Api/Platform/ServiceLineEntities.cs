namespace Tsdt.Api.Platform;

public sealed class ServiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class OrganizationServiceLine
{
    public Guid OrganizationId { get; set; }
    public Guid ServiceLineId { get; set; }
    public Organization Organization { get; set; } = null!;
    public ServiceLine ServiceLine { get; set; } = null!;
}
