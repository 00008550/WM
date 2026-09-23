using Microsoft.EntityFrameworkCore;
using WM.Modules.People.Domain;

namespace WM.Modules.People.Data;

public sealed class PeopleDbContext(DbContextOptions<PeopleDbContext> options) : DbContext(options)
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<LeavingReason> LeavingReasons => Set<LeavingReason>();
    public DbSet<LeaveNoticePeriod> LeaveNoticePeriods => Set<LeaveNoticePeriod>();

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

        // Same shape as LeavingReason, deliberately: legacy's dbo.LeaveNoticePeriods is the same
        // two-column customer vocabulary, and a second lookup that behaves differently from the first
        // is a trap. Schema only in 007 P1 — nothing writes it yet.
        b.Entity<LeaveNoticePeriod>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Employee>(e =>
        {
            // NO HasIndex on Code here, deliberately. The uniqueness rule is case-insensitive
            // ('E1030' and 'e1030' are one badge number) and the constraint that holds it is the
            // expression index UX_Employees_Code_Lower on lower("Code"), created in raw SQL by the
            // EmployeeCodeCaseInsensitive migration (007 P3) — EF's fluent API cannot express an
            // expression index. A plain unique index on Code would be the case-SENSITIVE rule the
            // application code disagreed with. Every lookup by code goes through EmployeeCode.Normalise
            // so the query and the index compare the same thing.
            // Optimistic concurrency (011 P5). The token goes into the UPDATE's WHERE clause, so a
            // write carrying the value another manager already replaced matches no row and EF raises
            // DbUpdateConcurrencyException — which PeopleModule turns into a 409.
            e.Property(x => x.Version).IsConcurrencyToken();
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

            e.HasOne<LeaveNoticePeriod>()
                .WithMany()
                .HasForeignKey(x => x.LeaveNoticePeriodId)
                .OnDelete(DeleteBehavior.Restrict);

            // Employment is computed from EmployedFrom/EmployedUntil/IsSuspended at a reference date,
            // so there is nothing to map: FullName and the status are answers, not columns.
            e.Ignore(x => x.FullName);
        });
    }
}
