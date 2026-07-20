namespace WM.SharedKernel.Events;

/// <summary>
/// Append-only event stream abstraction (Kafka in production).
/// Modules publish integration events here; consumers (dashboards, connectors,
/// audit) subscribe independently. A no-op/in-memory implementation keeps
/// local development working without brokers running.
/// </summary>
public interface IEventStreamProducer
{
    Task PublishAsync<TEvent>(string topic, string key, TEvent @event, CancellationToken ct = default)
        where TEvent : class;
}

public static class EventTopics
{
    public const string Punches = "wm.punches";
    public const string Audit = "wm.audit";
    public const string Integration = "wm.integration";
}
