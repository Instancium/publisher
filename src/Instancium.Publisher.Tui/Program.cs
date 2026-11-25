using Instancium.Publisher.Application.DependencyInjection;
using Instancium.Publisher.Infrastructure.DependencyInjection;
using Instancium.Publisher.Tui.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Instancium.Publisher.Tui;

internal static class Program
{
    internal static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddUserSecrets(typeof(Program).Assembly, optional: true);
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddPublisherApplication();
                services.AddPublisherInfrastructure(context.Configuration);
                services.AddPublisherTui(context.Configuration);
            })
            .Build();

        var orchestrator = host.Services.GetRequiredService<MainTuiOrchestrator>();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        await orchestrator.RunInteractiveLoopAsync(lifetime.ApplicationStopping);
    }
}