namespace Buteco.Workers;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Worker starting at: {time}", DateTimeOffset.Now);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Worker stopping at: {time}", DateTimeOffset.Now);
        return base.StopAsync(cancellationToken);
    }
}
