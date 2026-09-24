using Microsoft.EntityFrameworkCore;
using WM.Modules.TimeAttendance.Contracts;
using WM.Modules.TimeAttendance.Data;
using WM.Modules.TimeAttendance.Domain;

namespace WM.Modules.TimeAttendance.Services;

/// <summary>TimeAttendance's own implementation of <see cref="IDayTemplateDirectory"/> — reads only its own schema.</summary>
public sealed class DayTemplateDirectory(TimeAttendanceDbContext db) : IDayTemplateDirectory
{
    public async Task<EffectiveDayTemplate?> ResolveAsync(Guid employeeId, DateOnly date, Guid dayTemplateId, CancellationToken ct)
    {
        var day = await db.DayTemplates.AsNoTracking()
            .Include(t => t.ShiftMatchingRules)
            .SingleOrDefaultAsync(t => t.Id == dayTemplateId, ct);
        if (day is null) return null;

        DayTemplate? master = null;
        if (day.OverriddenByMasterTemplate)
        {
            var assignments = await db.MasterTemplateAssignments.AsNoTracking()
                .Where(a => a.EmployeeId == employeeId)
                .ToListAsync(ct);
            if (MasterTemplateAssignment.SelectFor(assignments, date) is { } active)
                master = await db.DayTemplates.AsNoTracking().SingleAsync(t => t.Id == active.MasterTemplateId, ct);
        }

        return EffectiveDayTemplate.Resolve(day, master);
    }
}
