using Microsoft.EntityFrameworkCore;
using WM.Modules.Identity.Domain;

namespace WM.Modules.Identity.Data;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Group> Groups => Set<Group>();

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
            e.HasMany(x => x.Groups).WithOne().HasForeignKey(x => x.UserId);
            e.HasMany(x => x.RefreshTokens).WithOne().HasForeignKey(x => x.UserId);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(88);
        });

        b.Entity<Group>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Description).HasMaxLength(512);
            e.HasMany(x => x.Departments).WithOne().HasForeignKey(x => x.GroupId);
            e.HasMany(x => x.Sites).WithOne().HasForeignKey(x => x.GroupId);
            e.HasMany(x => x.Employees).WithOne().HasForeignKey(x => x.GroupId);
            e.HasMany(x => x.ScreenPermissions).WithOne().HasForeignKey(x => x.GroupId);
            e.HasMany(x => x.Members).WithOne(x => x.Group).HasForeignKey(x => x.GroupId);
        });

        b.Entity<GroupDepartment>().HasKey(x => new { x.GroupId, x.DepartmentId });
        b.Entity<GroupSite>().HasKey(x => new { x.GroupId, x.SiteId });
        b.Entity<GroupEmployee>().HasKey(x => new { x.GroupId, x.EmployeeId });

        b.Entity<GroupScreenPermission>(e =>
        {
            e.HasKey(x => new { x.GroupId, x.ScreenId });
            e.Property(x => x.ScreenId).HasMaxLength(64);
        });

        b.Entity<UserGroup>(e =>
        {
            e.HasKey(x => new { x.UserId, x.GroupId });
        });
    }
}
