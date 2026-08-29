using Microsoft.EntityFrameworkCore;
using WM.Modules.Identity.Domain;

namespace WM.Modules.Identity.Data;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SecurityGroup> SecurityGroups => Set<SecurityGroup>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("identity");

        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.UserName).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            // Optimistic concurrency (011 P5), enforced because PUT /api/users/{id} reads the
            // caller's echoed token and UserManagementService.UpdateAsync answers the conflict.
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.UserName).HasMaxLength(128);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.HasMany(x => x.Roles).WithOne().HasForeignKey(x => x.UserId);
            e.HasMany(x => x.RefreshTokens).WithOne().HasForeignKey(x => x.UserId);
        });

        b.Entity<Role>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(128);
            e.HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.RoleId);
        });

        b.Entity<UserRole>(e =>
        {
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
        });

        b.Entity<RolePermission>(e =>
        {
            e.HasKey(x => new { x.RoleId, x.Permission });
            e.Property(x => x.Permission).HasMaxLength(128);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(88);
        });

        b.Entity<SecurityGroup>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Description).HasMaxLength(512);
            // The Version column exists here because SecurityGroup is an AuditableEntity, but it is
            // deliberately NOT a concurrency token yet. PUT /api/security-groups/{id} does not read
            // an echoed token, so enforcing it could only fire on a race *inside* one request — and
            // SecurityGroupService has no answer for DbUpdateConcurrencyException, so that would be
            // a 500 rather than the 409 this work is for. The column is here and unused on purpose;
            // wiring the endpoint is a follow-up, recorded in ConcurrencyTokenInventoryTests.
            e.HasMany(x => x.Sites).WithOne().HasForeignKey(x => x.SecurityGroupId);
            e.HasMany(x => x.Departments).WithOne().HasForeignKey(x => x.SecurityGroupId);
            e.HasMany(x => x.Constraints).WithOne().HasForeignKey(x => x.SecurityGroupId);
            e.HasMany(x => x.Members).WithOne(x => x.SecurityGroup).HasForeignKey(x => x.SecurityGroupId);
        });

        b.Entity<SecurityGroupSite>().HasKey(x => new { x.SecurityGroupId, x.SiteId });
        b.Entity<SecurityGroupDepartment>().HasKey(x => new { x.SecurityGroupId, x.DepartmentId });

        b.Entity<SecurityGroupConstraint>(e =>
        {
            // One constraint per dimension per group — a group cannot narrow the same axis twice.
            e.HasKey(x => new { x.SecurityGroupId, x.Dimension });
            e.HasMany(x => x.Values)
                .WithOne()
                .HasForeignKey(x => new { x.SecurityGroupId, x.Dimension });
        });

        b.Entity<SecurityGroupConstraintValue>()
            .HasKey(x => new { x.SecurityGroupId, x.Dimension, x.ValueId });

        b.Entity<UserSecurityGroup>(e =>
        {
            e.HasKey(x => new { x.UserId, x.SecurityGroupId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });
    }
}
