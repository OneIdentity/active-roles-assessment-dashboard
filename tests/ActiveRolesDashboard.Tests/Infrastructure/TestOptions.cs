using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Tests.Infrastructure;

/// <summary>Minimal IOptionsMonitor whose value can be swapped to simulate configuration reloads.</summary>
public sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = value;
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>IHttpClientFactory returning clients with a bounded timeout.</summary>
public sealed class TestHttpClientFactory(TimeSpan? timeout = null) : IHttpClientFactory
{
    public List<string> RequestedNames { get; } = new();

    public HttpClient CreateClient(string name)
    {
        RequestedNames.Add(name);
        return new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
    }
}
