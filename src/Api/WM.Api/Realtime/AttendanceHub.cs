using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WM.SharedKernel.Security;

namespace WM.Api.Realtime;

/// <summary>
/// Live channel for the attendance dashboard: punch feed + presence changes.
///
/// Authorization is two layers, and both are load-bearing:
/// <list type="number">
/// <item><b>Permission.</b> The policy below requires <c>attendance.view</c>, so a
/// self-service-only account cannot hold a socket at all. A bare <c>[Authorize]</c> here is what
/// made every authenticated user a recipient of the whole estate's punch stream
/// (PHASE-AUDIT.md A1).</item>
/// <item><b>Data scope.</b> Holding the permission says nothing about <i>whose</i> punches you
/// may see. <see cref="AttendanceAudience"/> puts the connection into the groups its resolved
/// scope earns, and the producer addresses punches to groups — so an in-scope manager and an
/// out-of-scope one both connect, and only one of them hears a given punch.</item>
/// </list>
/// A connection that earns no group hears nothing. That is the deliberate default: a mistake in
/// this file is silent — nothing errors, the wrong people simply keep seeing things — so the
/// failure has to fall on the side of showing too little.
/// </summary>
[Authorize(WmPermissions.AttendanceView)]
public sealed class AttendanceHub(AttendanceAudience audience) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await audience.SubscribeAsync(Context.ConnectionId, WmClaims.UserIdOf(Context.User), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await audience.UnsubscribeAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Re-derive this connection's audience.
    ///
    /// The server pushes <c>scopeChanged</c> when it moves a socket; this is the other
    /// direction — a client that refreshed its token or reconnected can ask to be re-placed
    /// without dropping the socket. It can only ever narrow or widen the caller's <i>own</i>
    /// connection to what the resolver says, so it needs no argument and grants nothing.
    /// </summary>
    public Task Reauthorize() =>
        audience.SubscribeAsync(Context.ConnectionId, WmClaims.UserIdOf(Context.User), Context.ConnectionAborted);
}
