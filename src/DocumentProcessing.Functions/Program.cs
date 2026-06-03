using Azure.Messaging.ServiceBus;
using DocumentProcessing.Core.Services;
using DocumentProcessing.Functions.Configuration;
using DocumentProcessing.Functions.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(context =>
    {
        // Register authentication middleware — runs before every function invocation.
        context.UseMiddleware<AuthenticationMiddleware>();
    })
    .ConfigureServices((hostContext, services) =>
    {
        // Bind authentication options from configuration
        services.AddOptions<AuthOptions>()
            .Bind(hostContext.Configuration.GetSection(AuthOptions.SectionName));

        // Register ServiceBusClient as singleton for programmatic message publishing
        services.AddSingleton(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>()
                .GetValue<string>("ServiceBusConnection");
            return new ServiceBusClient(connectionString);
        });

        // Register stub implementations — replace with real services in production.
        services.AddSingleton<IClassificationService, StubClassificationService>();
        services.AddSingleton<IOcrService, StubOcrService>();
        services.AddSingleton<IMetadataEnrichmentService, StubMetadataEnrichmentService>();
    })
    .Build();

await host.RunAsync();
