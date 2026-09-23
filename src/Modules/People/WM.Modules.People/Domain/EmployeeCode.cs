using System.Linq.Expressions;

namespace WM.Modules.People.Domain;

/// <summary>
/// The one employee-code rule (007 P3): codes are compared <b>case-insensitively</b> and
/// <b>without</b> folding leading zeros — <c>E1030</c> is <c>e1030</c>, but <c>42</c> is not
/// <c>0042</c> (a deliberate divergence from legacy's padded compare, plan 007 open question 1).
///
/// <para>
/// The database holds the same rule: the unique index <c>UX_Employees_Code_Lower</c> is on
/// <c>lower("Code")</c>, and <see cref="Matches"/> translates to <c>lower("Code") = @code</c>, so the
/// pre-checks, the directory lookup and the constraint all compare the same value. Change one and
/// the others must follow.
/// </para>
/// </summary>
public static class EmployeeCode
{
    /// <summary>The comparison key for a code as supplied by a caller.</summary>
    public static string Normalise(string code) => code.Trim().ToLowerInvariant();

    /// <summary>Employees whose code is <paramref name="code"/> under the rule above.</summary>
    public static Expression<Func<Employee, bool>> Matches(string code)
    {
        var key = Normalise(code);
        return e => e.Code.ToLower() == key;
    }
}
