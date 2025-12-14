using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OutboxDispatcher.Worker;
using OutboxDispatcher.Configuration;
using Microsoft.Extensions.Configuration;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((ctx, config) =>
    {
        // explicit load so file is used in Docker and local runs
        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
    })
    .ConfigureServices((ctx, services) =>
    {
        // bind strongly typed configuration for the worker
        services.Configure<OutboxOptions>(ctx.Configuration.GetSection("Outbox"));

        services.AddHttpClient();
        services.AddHostedService<OutboxWorker>();
    })
    .Build();

await host.RunAsync();