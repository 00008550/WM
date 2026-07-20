using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace WM.Api.Realtime;

/// <summary>Live channel for the attendance dashboard: punch feed + presence changes.</summary>
[Authorize]
public sealed class AttendanceHub : Hub;
