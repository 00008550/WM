using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.SignalR;
using WM.Api.Realtime;
using WM.Modules.TimeAttendance.Contracts;
using WM.SharedKernel.Events;

namespace WM.Api.Infrastructure;

/// <summary>
/// Publishes integration events to Kafka. If no broker is configured/reachable the
/// publish is skipped with a warning so local development works without Docker.
/// </summary>
public sealed class KafkaEventStreamProducer : IEventStreamProducer, IDisposable
{
    private readonly IProducer<string, string>? _producer;
    private readonly ILogger<KafkaEventStreamProducer> _logger;

    public KafkaEventStreamProducer(IConfiguration configuration, ILogger<KafkaEventStreamProducer> logger)
    {
        _logger = logger;
        var bootstrapServers = configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            _logger.LogWarning("Kafka:BootstrapServers not configured — event stream publishing disabled");
            return;
        }

        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.Leader,
            MessageTimeoutMs = 3000,
            SocketTimeoutMs = 3000,
        }).Build();
    }

    public Task PublishAsync<TEvent>(string topic, string key, TEvent @event, CancellationToken ct = default)
        where TEvent : class
    {
        if (_producer is null)
            return Task.CompletedTask;

        var payload = JsonSerializer.Serialize(@event);
        // Fire-and-forget with error callback: a broker outage must never fail the
        // user's request. Produce still throws synchronously when librdkafka's local
        // queue is full (unreachable or slow broker), so it must be guarded too.
        try
        {
            _producer.Produce(topic, new Message<string, string> { Key = key, Value = payload }, report =>
            {
                if (report.Error.IsError)
                    _logger.LogWarning("Kafka publish to {Topic} failed: {Reason}", topic, report.Error.Reason);
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kafka publish to {Topic} was dropped", topic);
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try { _producer?.Flush(TimeSpan.FromSeconds(2)); } catch { /* shutting down */ }
        _producer?.Dispose();
    }
}

/// <summary>
/// Decorator: every event goes to Kafka (durable stream for workers/connectors)
/// and to SignalR (instant UI update). Consumers that need guaranteed delivery
/// read Kafka; the UI channel is best-effort by design.
///
/// Both legs are non-blocking and non-throwing. Once a punch is persisted the
/// user's request must succeed — fan-out is a side effect, never a dependency.
///
/// The SignalR leg is <b>scoped</b>: it addresses the groups the punched employee belongs to,
/// not every connected client. Kafka is unfiltered on purpose — it is the durable estate-wide
/// stream that workers and connectors read, and it has no browser on the other end.
/// </summary>
public sealed class BroadcastingEventStreamProducer(
    KafkaEventStreamProducer kafka,
    IHubContext<AttendanceHub> hub,
    ILogger<BroadcastingEventStreamProducer> logger) : IEventStreamProducer
{
    /// <summary>
    /// Upper bound on the realtime leg. <c>SendAsync</c> awaits delivery to every
    /// connected client, so a single stalled browser socket would otherwise hang
    /// the request that triggered it.
    /// </summary>
    private static readonly TimeSpan BroadcastTimeout = TimeSpan.FromSeconds(2);

    public async Task PublishAsync<TEvent>(string topic, string key, TEvent @event, CancellationToken ct = default)
        where TEvent : class
    {
        try
        {
            await kafka.PublishAsync(topic, key, @event, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Event stream publish to {Topic} failed; continuing", topic);
        }

        if (topic != EventTopics.Punches)
            return;

        // Addressed, never broadcast. The groups come from the *punched employee's* own site,
        // department and identity; a connection is in one of them only if its user's resolved
        // scope contains that employee (AttendanceScopeGroups). This is the realtime half of the
        // rule PunchService already applies to GET /api/punches/recent — "the feed would leak the
        // existence and movements of out-of-scope staff" — which the realtime leg used to ignore.
        if (@event is not PunchRecorded punch)
        {
            // An unrecognised payload on the punch topic has no audience rule, so it gets no
            // audience. Falling back to everyone is the defect this portion exists to remove.
            logger.LogWarning("Event of type {EventType} on {Topic} has no realtime audience rule; not pushed",
                typeof(TEvent).Name, topic);
            return;
        }

        var audience = AttendanceScopeGroups.ForSubject(punch.EmployeeId, punch.SiteId, punch.DepartmentId);

        try
        {
            // Not linked to the request token: the caller's response may already have
            // been sent, and cancelling the push for that reason is not useful.
            using var cts = new CancellationTokenSource(BroadcastTimeout);
            await hub.Clients.Groups(audience).SendAsync("punchRecorded", @event, cts.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Realtime push timed out after {Timeout}s; clients will refresh on poll",
                BroadcastTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Realtime push failed; clients will refresh on poll");
        }
    }
}
