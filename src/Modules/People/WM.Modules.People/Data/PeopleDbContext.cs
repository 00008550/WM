using Microsoft.EntityFrameworkCore;
using WM.Modules.People.Domain;

namespace WM.Modules.People.Data;

public sealed class PeopleDbContext(DbContextOptions<PeopleDbContext> options) : DbContext(options)
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<LeavingReason> LeavingReasons => Set<LeavingReason>();

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

        b.Entity<LeavingReason>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200); // legacy dbo.LeaveReasons.Name is nvarchar(200)
            e.HasIndex(x => x.Name).IsUnique();
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
            e.Property(x => x.LeaverComments).HasMaxLength(500); // legacy AdditionalLeaverComments width
            e.HasIndex(x => x.SiteId);

            // Restrict, not cascade or set-null: a reason in use must be retired via IsActive rather
            // than deleted, and the database is what makes that true instead of merely intended.
            e.HasOne<LeavingReason>()
                .WithMany()
                .HasForeignKey(x => x.LeavingReasonId)
                .OnDelete(DeleteBehavior.Restrict);

            // Employment is computed from EmployedFrom/EmployedUntil/IsSuspended at a reference date,
            // so there is nothing to map: FullName and the status are answers, not columns.
            e.Ignore(x => x.FullName);
        });
    }
}
