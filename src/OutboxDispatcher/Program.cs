using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OutboxDispatcher.Worker;
using System;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        services.AddHttpClient();
        services.AddHostedService<OutboxWorker>();
    })
    .Build();

await host.RunAsync();
