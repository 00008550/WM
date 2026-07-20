using WM.SharedKernel.Domain;

namespace WM.Modules.People.Domain;

public sealed class Site : Entity
{
    public required string Name { get; set; }
    public Guid? ParentId { get; set; }
    public string TimeZone { get; set; } = "UTC";

    public List<Site> Children { get; set; } = [];
}

public sealed class Department : Entity
{
    public required string Name { get; set; }
    public Guid SiteId { get; set; }
}

public enum EmployeeStatus
{
    Active = 0,
    OnLeave = 1,
    Terminated = 2,
}

public sealed class Employee : AuditableEntity
{
    /// <summary>Badge/payroll number — unique per installation, used by devices.</summary>
    public required string Code { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? JobTitle { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public DateOnly HireDate { get; set; }
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    public string FullName => $"{FirstName} {LastName}";
}
