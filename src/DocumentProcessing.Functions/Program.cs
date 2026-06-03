using DocumentProcessing.Contracts.Models;
using DocumentProcessing.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // Register stub implementations — replace with real services in production.
        services.AddSingleton<IClassificationService, StubClassificationService>();
        services.AddSingleton<IOcrService, StubOcrService>();
        services.AddSingleton<IMetadataEnrichmentService, StubMetadataEnrichmentService>();
    })
    .Build();

await host.RunAsync();
