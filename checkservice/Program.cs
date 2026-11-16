using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "WinSecMon")
    .ConfigureServices(services =>
    {
        services.AddHostedService<Worker>();
        services.AddSingleton<TeamsNotifier>();
        services.AddSingleton<FileIntegrityChecker>();
        services.AddSingleton<SystemHealthChecker>();
        services.AddSingleton<RdpSessionTracker>();
    });

var host = builder.Build();
var notifier = host.Services.GetRequiredService<TeamsNotifier>();
var configuration = host.Services.GetRequiredService<IConfiguration>();
var serverName = configuration.GetValue<string>("ServerInfo:ServerName", Environment.MachineName);
var companyName = configuration.GetValue<string>("ServerInfo:CompanyName", "Não informado");

await notifier.SendStartupNotificationAsync(serverName, companyName);
await host.RunAsync();
