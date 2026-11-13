using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace ActiveRolesDashboard.Tests.Infrastructure;

/// <summary>
/// Starts the dashboard in memory with an isolated, per-instance content root.
/// All writable state (appsettings.json, App_Data, usersettings) lives in a temp folder that is
/// deleted on dispose, so tests never touch the repository or each other.
/// </summary>
public sealed class DashboardAppFactory : WebApplicationFactory<Program>
{
    private readonly TestContentRoot _contentRoot;

    public DashboardAppFactory(JsonObject? appSettings = null, FakeActiveRolesServer? activeRoles = null)
    {
        _contentRoot = new TestContentRoot();
        ActiveRoles = activeRoles;
        var settings = appSettings ?? AppSettingsBuilder.Default(activeRoles).Build();
        _contentRoot.WriteAppSettings(settings);
    }

    public FakeActiveRolesServer? ActiveRoles { get; }
    public TestContentRoot ContentRoot => _contentRoot;

    /// <summary>Clock used by the app. Advance it to test refresh schedules, retention and expiry.</summary>
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));

    /// <summary>Hosted services (startup cache load, performance sampler) are off unless enabled.</summary>
    public bool EnableHostedServices { get; init; }

    /// <summary>Extra service overrides applied after the app's own registrations.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(_contentRoot.Path);
        builder.UseWebRoot(Path.Combine(RepositoryPaths.ProjectDirectory, "wwwroot"));

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);

            if (!EnableHostedServices)
                services.RemoveAll<IHostedService>();

            ConfigureServices?.Invoke(services);
        });
    }

    /// <summary>Client that does not follow redirects, so tests can assert on them.</summary>
    public HttpClient CreateNonRedirectingClient()
        => CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _contentRoot.Dispose();
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var d in services.Where(d => d.ServiceType == typeof(T)).ToList())
            services.Remove(d);
    }
}
