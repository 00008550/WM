using WM.SharedKernel.Security;

namespace WM.Modules.People.Services;

/// <summary>
/// The write half of the employee scope rule: may this caller file an employee record <i>here</i>?
///
/// <para>
/// Reads go through <see cref="EmployeeScopeExtensions.WithinScope"/>, which is a query filter. A
/// write cannot use it: on a create there is no row to filter, and on an update the image that
/// matters is the one the request asks for rather than the one on disk. So this is a predicate over
/// a <b>candidate image</b> — and deliberately not a second copy of the rule. It delegates to
/// <see cref="EffectiveDataScope.CanSee"/>, the same switch <c>WithinScope</c> expresses as SQL,
/// which 003 P1 already uses this way for the realtime feed (<c>AttendanceScopeGroups</c>).
/// <c>EmployeeWriteScopeTests</c> pins the predicate against the filter over every scope shape, so
/// a divergence fails a test instead of leaking quietly.
/// </para>
///
/// <para>
/// Measured, not assumed (<c>docs/TLW-AUTHORIZATION-MODEL.md</c> §5): legacy's read scope <i>is</i>
/// its write scope — <c>CanCurrentUserAccessEmployee</c> resolves both through one
/// <c>dbo.EmployeeIdsManagedByRole</c> call, and <c>AccessType</c> changes the answer in exactly one
/// case (<c>AuthorizationService.cs:281-286</c>). WM therefore reuses the read set rather than
/// inventing a write model. What legacy never checks is the <b>post-image</b>: a legacy manager can
/// edit someone they can see and move them out of their own scope. Refusing that is WM's addition
/// (003 decision 3), with no legacy behaviour to preserve.
/// </para>
/// </summary>
public static class EmployeeWriteScope
{
    /// <summary>
    /// Whether <paramref name="scope"/> permits an employee record to exist as described — the
    /// post-image of a create or an update.
    ///
    /// <para>
    /// <paramref name="employeeId"/> is <c>null</c> for a create, where no record exists yet. It is
    /// the record's own id for an update, because two of the four scope arms are answered by the
    /// employee rather than by their site or department.
    /// </para>
    ///
    /// <para>
    /// This is only ever the <i>destination</i> check. On an update the caller must also pass the
    /// pre-image check (<c>WithinScope</c>, which 404s a record they cannot see) — you may not edit
    /// whom you cannot see, and you may not move whom you can edit out of your own sight.
    /// </para>
    /// </summary>
    public static bool PermitsWrite(
        this EffectiveDataScope scope, Guid? employeeId, Guid siteId, Guid? departmentId)
    {
        // A create has no id yet, and Guid.Empty is never a real one — Entity.Id is a v7 GUID. So a
        // Self scope, whose only grant is an id match, correctly creates nobody.
        var subject = employeeId ?? Guid.Empty;

        if (!scope.CanSee(subject, siteId, departmentId))
            return false;

        // The one place read and write are allowed to disagree, and today they do not.
        return subject != scope.SelfEmployeeId || scope.CanEditOwnRecord();
    }

    /// <summary>
    /// Whether the caller may edit their <b>own</b> employee record. Legacy's
    /// <c>Role.CanModifySelf</c>: "may see your own record, may not edit it"
    /// (<c>AuthorizationService.cs:281-286</c>) — the only genuine read/write asymmetry in legacy's
    /// record layer, and the whole reason its <c>AccessType</c> parameter exists.
    ///
    /// <para>
    /// Adopted by the user 2026-08-05 and not yet representable: no security group carries the flag.
    /// It is a named predicate returning <c>true</c> rather than an absent check so that wiring it
    /// up is one line — 005 P1 adds <c>SecurityGroup.CanEditOwnRecord</c> and 005 P4 carries it onto
    /// the resolved scope, at which point this returns that flag instead of the constant.
    /// </para>
    ///
    /// <para>
    /// Note for whoever does that: today's resolver drops the link for the widest scope —
    /// <c>EffectiveDataScope.All()</c> has a null <c>SelfEmployeeId</c>
    /// (<c>DataScope.cs:36-37</c>), so an employee-linked administrator never reaches this
    /// predicate at all. That is the old model's shape, not a decision made here.
    /// </para>
    /// </summary>
    public static bool CanEditOwnRecord(this EffectiveDataScope scope) => true;
}
