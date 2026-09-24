using Microsoft.EntityFrameworkCore;
using WM.Modules.TimeAttendance.Domain;

namespace WM.Modules.TimeAttendance.Data;

public sealed class TimeAttendanceDbContext(DbContextOptions<TimeAttendanceDbContext> options) : DbContext(options)
{
    public DbSet<Punch> Punches => Set<Punch>();
    public DbSet<DayTemplate> DayTemplates => Set<DayTemplate>();
    public DbSet<ShiftMatchingRule> ShiftMatchingRules => Set<ShiftMatchingRule>();
    public DbSet<MasterTemplateAssignment> MasterTemplateAssignments => Set<MasterTemplateAssignment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("time_attendance");

        b.Entity<Punch>(e =>
        {
            e.Property(x => x.EmployeeCode).HasMaxLength(32);
            e.Property(x => x.DeviceId).HasMaxLength(128);
            e.HasIndex(x => new { x.EmployeeId, x.Timestamp });
            e.HasIndex(x => x.Timestamp);
            // 008 P4: the timesheet reads by frozen local day.
            e.Property(x => x.LocalZone).HasMaxLength(64);
            e.HasIndex(x => new { x.EmployeeId, x.LocalDate });
        });

        // Plan 010 P1 — the allocation subset of legacy dbo.DailyModels and its two child tables.
        b.Entity<DayTemplate>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(128);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasMany(x => x.ShiftMatchingRules).WithOne()
                .HasForeignKey(x => x.DayTemplateId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ShiftMatchingRule>(e =>
        {
            // The target template must outlive the rule that names it.
            e.HasOne<DayTemplate>().WithMany()
                .HasForeignKey(x => x.TemplateToAssignId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<MasterTemplateAssignment>(e =>
        {
            // EmployeeId is People's — indexed, never a foreign key (invariant 1).
            e.HasIndex(x => x.EmployeeId);
            e.HasOne<DayTemplate>().WithMany()
                .HasForeignKey(x => x.MasterTemplateId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_master_template_assignments_window",
                "\"StartDate\" IS NULL OR \"EndDate\" IS NULL OR \"StartDate\" <= \"EndDate\""));
        });
    }
}
