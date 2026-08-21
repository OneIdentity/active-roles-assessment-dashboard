using System.Diagnostics;
using GraphPerfTest;
using Microsoft.Extensions.Configuration;

// Load configuration from appsettings.json (copied to output) plus optional user secrets
// via environment variables, so client secrets need not be committed.
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables("GRAPHPERF_")
    .Build();

var settings = config.Get<AppSettings>() ?? new AppSettings();

if (settings.Tenants.Count == 0)
{
    Console.Error.WriteLine("No tenants configured. Add at least one tenant to appsettings.json.");
    return 1;
}

Console.WriteLine("Microsoft Graph Entra retrieval performance test");
Console.WriteLine($"Tenants: {settings.Tenants.Count}, IncludeGroupMembers: {settings.IncludeGroupMembers}, " +
                  $"MaxObjectsPerType: {(settings.MaxObjectsPerType == 0 ? "unlimited" : settings.MaxObjectsPerType.ToString())}");

var overall = Stopwatch.StartNew();

foreach (var tenant in settings.Tenants)
{
    if (string.IsNullOrWhiteSpace(tenant.TenantId) || string.IsNullOrWhiteSpace(tenant.ClientId)
        || (string.IsNullOrWhiteSpace(tenant.ClientSecret) && !tenant.UsesCertificate))
    {
        Console.Error.WriteLine(
            $"Skipping tenant '{tenant.Name}': need TenantId, ClientId and either a ClientSecret " +
            "or a CertificateThumbprint/CertificatePath.");
        continue;
    }

    try
    {
        var benchmark = new GraphBenchmark(tenant, settings);
        await benchmark.RunAsync();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Tenant '{tenant.Name}' failed: {ex.GetType().Name}: {ex.Message}");
    }
}

overall.Stop();
Console.WriteLine();
Console.WriteLine($"Total elapsed: {overall.ElapsedMilliseconds:N0} ms");
return 0;
