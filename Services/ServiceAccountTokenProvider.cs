using ActiveRolesDashboard.Models;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Services;

/// <summary>
/// Acquires and caches an Active Roles access token for the collection SERVICE ACCOUNT
/// (not an interactive user). Used by the background superset loader, which has no user
/// context. The password is read encrypted from configuration and decrypted via
/// <see cref="ServiceAccountSecretProtector"/>; the token is cached and refreshed shortly
/// before it expires.
/// </summary>
public class ServiceAccountTokenProvider
{
    // Refresh the token this long before its stated expiry to avoid using a token that
    // expires mid-request.
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(2);

    private readonly RstsAuthService _authService;
    private readonly ServiceAccountSecretProtector _protector;
    private readonly IOptionsMonitor<ActiveRolesConfig> _config;
    private readonly ILogger<ServiceAccountTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;

    public ServiceAccountTokenProvider(
        RstsAuthService authService,
        ServiceAccountSecretProtector protector,
        IOptionsMonitor<ActiveRolesConfig> config,
        ILogger<ServiceAccountTokenProvider> logger)
    {
        _authService = authService;
        _protector = protector;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Returns a valid service-account access token, acquiring or refreshing it as needed.
    /// Throws <see cref="InvalidOperationException"/> if the service account is not configured
    /// or authentication fails.
    /// </summary>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (IsCurrent())
            return _cachedToken!;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the gate; another caller may have refreshed it.
            if (IsCurrent())
                return _cachedToken!;

            var sa = _config.CurrentValue.ServiceAccount;
            if (string.IsNullOrWhiteSpace(sa.Username))
                throw new InvalidOperationException(
                    "ActiveRoles:ServiceAccount:Username is not configured; cannot collect the shared superset.");

            var password = _protector.Unprotect(sa.ProtectedPassword);

            var result = await _authService.GetTokenAsync(sa.Username, password).ConfigureAwait(false);
            if (!result.Success || string.IsNullOrEmpty(result.AccessToken))
                throw new InvalidOperationException(
                    $"Service-account authentication failed: {result.Error ?? "unknown error"}.");

            _cachedToken = result.AccessToken;
            _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(result.ExpiresIn);
            _logger.LogInformation("Acquired service-account token for '{User}', valid until {Expiry:o}.",
                sa.Username, _expiresAtUtc);

            return _cachedToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forces the next <see cref="GetTokenAsync"/> to re-authenticate (e.g. after a 401).</summary>
    public void Invalidate()
    {
        _cachedToken = null;
        _expiresAtUtc = DateTimeOffset.MinValue;
    }

    private bool IsCurrent() =>
        !string.IsNullOrEmpty(_cachedToken) && DateTimeOffset.UtcNow < _expiresAtUtc - ExpirySkew;
}
