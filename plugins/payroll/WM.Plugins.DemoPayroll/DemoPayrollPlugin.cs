using System.Text;
using Microsoft.Extensions.Logging;
using WM.Plugins.Abstractions;

namespace WM.Plugins.DemoPayroll;

/// <summary>
/// Reference payroll-export plugin: writes timesheets as a simple CSV.
/// Real customer plugins follow this exact shape — one class, one contract.
/// </summary>
public sealed class DemoPayrollPlugin : IPayrollExportPlugin
{
    public string Id => "payroll.demo";

    public Task<PayrollExportResult> ExportAsync(PayrollExportContext context, CancellationToken ct = default)
    {
        context.Logger.LogInformation(
            "Demo payroll export for {Customer}: {Count} lines, {Start} → {End}",
            context.CustomerName, context.Lines.Count, context.PeriodStart, context.PeriodEnd);

        var csv = new StringBuilder("EmployeeCode;EmployeeName;Date;RegularHours;OvertimeHours;AbsenceHours;AbsenceCode\n");
        foreach (var line in context.Lines)
        {
            ct.ThrowIfCancellationRequested();
            csv.Append(line.EmployeeCode).Append(';')
               .Append(line.EmployeeName).Append(';')
               .Append(line.Date.ToString("yyyy-MM-dd")).Append(';')
               .Append(line.RegularHours).Append(';')
               .Append(line.OvertimeHours).Append(';')
               .Append(line.AbsenceHours).Append(';')
               .Append(line.AbsenceCode).Append('\n');
        }

        var file = new PayrollExportFile(
            $"payroll_{context.PeriodStart:yyyyMMdd}_{context.PeriodEnd:yyyyMMdd}.csv",
            Encoding.UTF8.GetBytes(csv.ToString()),
            "text/csv");

        return Task.FromResult(PayrollExportResult.Success(file));
    }
}
