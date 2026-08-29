namespace WM.SharedKernel.Domain;

/// <summary>
/// The one wording a refused stale write uses, wherever it is refused (011 P5, decision D4).
///
/// <para>
/// Shared because two endpoints in two modules answer the same question and a user who hits both
/// should not have to learn that they mean the same thing. It is a string constant on a
/// cross-cutting primitive, not a contract between the modules — neither module reads the other.
/// </para>
/// </summary>
public static class ConcurrentEdit
{
    /// <summary>
    /// Deliberately says <i>changed</i>, not <i>lost</i>. Two managers who saved the same phone
    /// number still collide (011 edge case 6) and nothing was lost in that case; a message that
    /// claimed otherwise would alarm the second one for no reason. And it says what to do, because
    /// the bar this has to clear is that the user reloads rather than retrying blindly (edge case 7)
    /// — legacy's answer here is an uncaught <c>ChangeConflictException</c>.
    /// </summary>
    public const string Message = "Someone else changed this record — reload and try again.";
}
