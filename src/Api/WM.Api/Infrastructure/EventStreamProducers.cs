using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.SignalR;
using WM.Api.Realtime;
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
        // Fire-and-forget with error callback: a broker outage must never fail the user's request.
        _producer.Produce(topic, new Message<string, string> { Key = key, Value = payload }, report =>
        {
            if (report.Error.IsError)
                _logger.LogWarning("Kafka publish to {Topic} failed: {Reason}", topic, report.Error.Reason);
        });
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
/// </summary>
public sealed class BroadcastingEventStreamProducer(
    KafkaEventStreamProducer kafka,
    IHubContext<AttendanceHub> hub) : IEventStreamProducer
{
    public async Task PublishAsync<TEvent>(string topic, string key, TEvent @event, CancellationToken ct = default)
        where TEvent : class
    {
        await kafka.PublishAsync(topic, key, @event, ct);
        if (topic == EventTopics.Punches)
            await hub.Clients.All.SendAsync("punchRecorded", @event, ct);
    }
}
