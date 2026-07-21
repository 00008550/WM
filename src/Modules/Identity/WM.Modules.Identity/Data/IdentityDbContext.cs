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
            e.HasMany(x => x.Sites).WithOne().HasForeignKey(x => x.SecurityGroupId);
            e.HasMany(x => x.Departments).WithOne().HasForeignKey(x => x.SecurityGroupId);
            e.HasMany(x => x.Members).WithOne(x => x.SecurityGroup).HasForeignKey(x => x.SecurityGroupId);
        });

        b.Entity<SecurityGroupSite>().HasKey(x => new { x.SecurityGroupId, x.SiteId });
        b.Entity<SecurityGroupDepartment>().HasKey(x => new { x.SecurityGroupId, x.DepartmentId });

        b.Entity<UserSecurityGroup>(e =>
        {
            e.HasKey(x => new { x.UserId, x.SecurityGroupId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });
    }
}
