using Microsoft.EntityFrameworkCore;
using WM.Modules.TimeAttendance.Domain;

namespace WM.Modules.TimeAttendance.Data;

public sealed class TimeAttendanceDbContext(DbContextOptions<TimeAttendanceDbContext> options) : DbContext(options)
{
    public DbSet<Punch> Punches => Set<Punch>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("time_attendance");

        b.Entity<Punch>(e =>
        {
            e.Property(x => x.EmployeeCode).HasMaxLength(32);
            e.Property(x => x.DeviceId).HasMaxLength(128);
            e.HasIndex(x => new { x.EmployeeId, x.Timestamp });
            e.HasIndex(x => x.Timestamp);
        });
    }
}
