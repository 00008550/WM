using Confluent.Kafka;

namespace WM.Worker.Kafka;

/// <summary>
/// Consumes the wm.punches stream. Today it just logs; this is where
/// downstream processing (rules engine triggers, connector fan-out) hooks in.
/// Resilient by design: if Kafka is unreachable it retries in the background
/// without ever crashing the worker.
/// </summary>
public sealed class PunchStreamConsumer(
    IConfiguration configuration,
    ILogger<PunchStreamConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrapServers = configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            logger.LogWarning("Kafka:BootstrapServers not configured — punch stream consumer disabled");
            return;
        }

        // Consume on a dedicated thread; Confluent's consumer is blocking.
        await Task.Run(() => ConsumeLoop(bootstrapServers, stoppingToken), stoppingToken);
    }

    private void ConsumeLoop(string bootstrapServers, CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = "wm-worker",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var consumer = new ConsumerBuilder<string, string>(config).Build();
                consumer.Subscribe("wm.punches");
                logger.LogInformation("Subscribed to wm.punches");

                while (!stoppingToken.IsCancellationRequested)
                {
                    var result = consumer.Consume(stoppingToken);
                    logger.LogInformation("Punch event key={Key}: {Value}", result.Message.Key, result.Message.Value);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Kafka consume loop error — retrying in 10s");
                if (stoppingToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
                    return;
            }
        }
    }
}
