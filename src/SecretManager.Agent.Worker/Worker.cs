using Microsoft.Extensions.Options;
using SecretManager.Agent.Worker.Configuration;
using SecretManager.Agent.Worker.Runtime;

namespace SecretManager.Agent.Worker;

public sealed class Worker(
    IAgentCoordinator coordinator,
    IOptions<AgentOptions> options,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.EnableBackgroundSync)
        {
            logger.LogInformation("Background synchronization is disabled by configuration.");
            return;
        }

        await coordinator.InitializeAsync(stoppingToken);
        var notificationTask = options.Value.EnableChangeNotifications
            ? RunNotificationLoopAsync(stoppingToken)
            : Task.CompletedTask;

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.SyncPollIntervalSeconds)));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await coordinator.SyncNowAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        try
        {
            await notificationTask;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunNotificationLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await coordinator.ListenForInvalidationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Agent notification stream failed; polling remains active as fallback.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Value.NotificationReconnectDelaySeconds)), stoppingToken);
            }
        }
    }
}
