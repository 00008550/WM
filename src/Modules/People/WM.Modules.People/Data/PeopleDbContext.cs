using Microsoft.EntityFrameworkCore;
using WM.Modules.People.Domain;

namespace WM.Modules.People.Data;

public sealed class PeopleDbContext(DbContextOptions<PeopleDbContext> options) : DbContext(options)
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("people");

        b.Entity<Site>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasMany(x => x.Children).WithOne().HasForeignKey(x => x.ParentId);
        });

        b.Entity<Department>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.SiteId, x.Name }).IsUnique();
        });

        b.Entity<Employee>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.FirstName).HasMaxLength(128);
            e.Property(x => x.LastName).HasMaxLength(128);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Phone).HasMaxLength(64);
            e.Property(x => x.JobTitle).HasMaxLength(128);
            e.HasIndex(x => x.SiteId);
            e.Ignore(x => x.FullName);
        });
    }
}
