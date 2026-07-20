namespace WM.SharedKernel.Domain;

public abstract class Entity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
}

public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}
