using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemoteSupport.Service;

[assembly: SupportedOSPlatform("windows")]

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<DeviceLifecycleWorker>();
builder.Services.AddSingleton(_ => new DeviceIdentityStore(Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "ASCOS", "RemoteSupport", "device-identity.json")));
await builder.Build().RunAsync();

internal sealed class DeviceLifecycleWorker(ILogger<DeviceLifecycleWorker> logger, DeviceIdentityStore identityStore) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var identity = await identityStore.LoadOrCreateAsync(stoppingToken);
        logger.LogInformation("ASCOS device lifecycle service started for device {DeviceId}. Screen capture remains in the per-user Session Agent.", identity.DeviceId);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
