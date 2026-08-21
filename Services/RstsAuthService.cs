using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ActiveRolesDashboard.Models;
using Microsoft.Extensions.Options;

namespace ActiveRolesDashboard.Services;

public class RstsAuthService
{
    private readonly ActiveRolesConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;

    public RstsAuthService(IOptions<ActiveRolesConfig> config, IHttpClientFactory httpClientFactory)
    {
        _config = config.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<TokenResult> GetTokenAsync(string username, string password)
    {
        var client = _httpClientFactory.CreateClient("RSTS");

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = password,
            ["resource"] = _config.Resource
        });

        try
        {
            var response = await client.PostAsync(_config.RstsUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return new TokenResult { Success = false, Error = $"Authentication failed: {response.StatusCode}" };
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
            return new TokenResult
            {
                Success = true,
                AccessToken = tokenResponse!.AccessToken,
                ExpiresIn = tokenResponse.ExpiresIn
            };
        }
        catch (Exception ex)
        {
            return new TokenResult { Success = false, Error = $"Connection error: {ex.Message}" };
        }
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}

public class TokenResult
{
    public bool Success { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string? Error { get; set; }
}
