namespace RemoteSupport.Signaling;

public sealed class SecurityStorePurgeService : BackgroundService
{
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromMinutes(5);
    private readonly SecurityStore _store;
    private readonly TimeProvider _clock;
    private readonly ILogger<SecurityStorePurgeService> _logger;

    public SecurityStorePurgeService(SecurityStore store, TimeProvider clock, ILogger<SecurityStorePurgeService> logger)
    {
        _store = store;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PurgeInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var removed = _store.PurgeExpired();
                if (removed > 0)
                    _logger.LogInformation("Purged {Count} expired security entries at {Timestamp:O}.", removed, _clock.GetUtcNow());
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Security store purge failed.");
            }
        }
    }
}
