namespace WM.SharedKernel.Domain;

public abstract class Entity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
}

public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    /// <summary>
    /// The optimistic-concurrency token. Read with the record, echoed on write, and rewritten by
    /// whichever write lands — so a second writer holding the value the first one replaced is
    /// refused rather than silently overwriting it (plan 011 P5, finding F8).
    ///
    /// <para>
    /// <b>An explicit mapped column, deliberately — never Npgsql's <c>xmin</c>.</b>
    /// <c>UseXminAsConcurrencyToken()</c> is the shorter spelling and it was rejected: <c>xmin</c> is
    /// a shadow property the Postgres storage engine populates, so under the in-memory provider
    /// nothing ever writes it, it sits at its default forever, and every concurrency test would pass
    /// <i>vacuously</i> — a green suite proving the opposite of what it claims. There is no Postgres
    /// test harness in this repository (<c>docs/plans/STATE.md</c>, 009 P4), so a token that only
    /// works on Postgres is a token nothing can test. The column is worth its 16 bytes.
    /// </para>
    /// <para>
    /// A row version, not legacy's per-column <c>UpdateCheck.Always</c> (148 of <c>dbo.Employees</c>'
    /// 153 columns). Full-column comparison would let two managers editing different fields both
    /// succeed — but every mutation endpoint in WM is a full-replace <c>PUT</c>, so a stale form does
    /// not edit one field, it writes them all. There is no such thing as a disjoint edit here.
    /// </para>
    /// <para>
    /// Set by the entity, not by the database: only the two endpoints that read the caller's echoed
    /// value enforce it (see each <c>DbContext</c>). Present on this base type means present on every
    /// auditable entity — currently <c>Employee</c>, <c>User</c> and <c>SecurityGroup</c>.
    /// <c>Punch</c> derives from <see cref="Entity"/> and is untouched, which is correct: a punch is
    /// append-only and nothing edits one.
    /// </para>
    /// </summary>
    public Guid Version { get; set; } = Guid.CreateVersion7();
}
