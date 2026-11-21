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

    /// <summary>Every request actually sent by clients from this factory, in send order.</summary>
    public System.Collections.Concurrent.ConcurrentQueue<(HttpMethod Method, Uri Uri)> SentRequests { get; } = new();

    public HttpClient CreateClient(string name)
    {
        RequestedNames.Add(name);
        return new HttpClient(new RecordingHandler(SentRequests)) { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
    }

    private sealed class RecordingHandler(System.Collections.Concurrent.ConcurrentQueue<(HttpMethod, Uri)> sink)
        : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            sink.Enqueue((request.Method, request.RequestUri!));
            return base.SendAsync(request, cancellationToken);
        }
    }
}
