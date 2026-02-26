using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SsoEntraId.Bff.NativeAuth;

public record NativeAuthStepResult(
    string ContinuationToken,
    string ChallengeType,
    int? CodeLength = null,
    string? MaskedEmail = null,
    string? Error = null,
    string? ErrorDescription = null);

public record NativeAuthTokenResult(
    string AccessToken,
    string IdToken,
    string RefreshToken,
    int ExpiresIn);

file sealed record EntraStepResponse(
    [property: JsonPropertyName("continuation_token")]    string? ContinuationToken,
    [property: JsonPropertyName("challenge_type")]         string? ChallengeType,
    [property: JsonPropertyName("code_length")]            int?    CodeLength,
    [property: JsonPropertyName("challenge_target_label")] string? ChallengeTargetLabel,
    [property: JsonPropertyName("error")]                  string? Error,
    [property: JsonPropertyName("error_description")]      string? ErrorDescription);

file sealed record EntraTokenResponse(
    [property: JsonPropertyName("access_token")]      string? AccessToken,
    [property: JsonPropertyName("id_token")]           string? IdToken,
    [property: JsonPropertyName("refresh_token")]      string? RefreshToken,
    [property: JsonPropertyName("expires_in")]         int     ExpiresIn,
    [property: JsonPropertyName("error")]              string? Error,
    [property: JsonPropertyName("error_description")]  string? ErrorDescription);

public sealed class EntraNativeAuthService(HttpClient httpClient, IConfiguration configuration)
{
    // Native auth requires a public client. Use a dedicated public client app registration
    // if the main BFF app (confidential client with secret) causes AADSTS550022.
    // Set "Entra:NativeAuthClientId" to the public client app's client ID,
    // or leave unset to fall back to the same app (requires "Allow public client flows" = Yes).
    private string ClientId =>
        configuration["Entra:NativeAuthClientId"]
        ?? configuration["AzureAd:ClientId"]
        ?? throw new InvalidOperationException("AzureAd:ClientId not configured");

    private string ApiScope => configuration["ApiScopes"] ?? "openid profile offline_access";

    public Task<NativeAuthStepResult> StartSignUpAsync(string email, CancellationToken ct = default)
        => PostStepAsync("signup/v1.0/start", new()
        {
            ["client_id"]      = ClientId,
            ["challenge_type"] = "oob password redirect",
            ["username"]       = email
        }, ct);

    public Task<NativeAuthStepResult> RequestOtpChallengeAsync(string continuationToken, CancellationToken ct = default)
        => PostStepAsync("signup/v1.0/challenge", new()
        {
            ["client_id"]          = ClientId,
            ["challenge_type"]     = "oob",
            ["continuation_token"] = continuationToken
        }, ct);

    public Task<NativeAuthStepResult> SubmitOtpAsync(string continuationToken, string otp, CancellationToken ct = default)
        => PostStepAsync("signup/v1.0/continue", new()
        {
            ["client_id"]          = ClientId,
            ["continuation_token"] = continuationToken,
            ["grant_type"]         = "oob",
            ["oob"]                = otp
        }, ct);

    public Task<NativeAuthStepResult> SubmitPasswordAsync(string continuationToken, string password, CancellationToken ct = default)
        => PostStepAsync("signup/v1.0/continue", new()
        {
            ["client_id"]          = ClientId,
            ["continuation_token"] = continuationToken,
            ["grant_type"]         = "password",
            ["password"]           = password
        }, ct);

    public async Task<NativeAuthTokenResult> ExchangeTokensAsync(string continuationToken, CancellationToken ct = default)
    {
        var scope = $"openid profile offline_access {ApiScope}";
        using var response = await httpClient.PostAsync("oauth2/v2.0/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"]          = ClientId,
                ["continuation_token"] = continuationToken,
                ["grant_type"]         = "continuation_token",
                ["scope"]              = scope
            }), ct);

        var rawBody = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(rawBody))
            throw new InvalidOperationException(
                $"Entra trả về response rỗng cho token endpoint (HTTP {(int)response.StatusCode}).");

        EntraTokenResponse? body;
        try { body = JsonSerializer.Deserialize<EntraTokenResponse>(rawBody); }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Non-JSON token response: {rawBody[..Math.Min(300, rawBody.Length)]}", ex);
        }

        if (body is null)
            throw new InvalidOperationException("Null token response from Entra");

        if (body.Error is not null)
            throw new InvalidOperationException(body.ErrorDescription ?? body.Error);

        return new(body.AccessToken!, body.IdToken!, body.RefreshToken!, body.ExpiresIn);
    }

    private async Task<NativeAuthStepResult> PostStepAsync(
        string path, Dictionary<string, string> fields, CancellationToken ct)
    {
        using var response = await httpClient.PostAsync(path, new FormUrlEncodedContent(fields), ct);
        var actualUrl = response.RequestMessage?.RequestUri?.ToString() ?? "(unknown)";
        var body = await response.Content.ReadAsStringAsync(ct);

        if (string.IsNullOrWhiteSpace(body))
            return new("", "", Error: "native_auth_unavailable",
                ErrorDescription:
                    $"Entra trả về response rỗng (HTTP {(int)response.StatusCode}). " +
                    $"URL đã gọi: {actualUrl}. " +
                    "Kiểm tra: (1) Enable native authentication trong Azure Portal → App Registration → Authentication. " +
                    "(2) Bật native auth trong User Flow → Properties. " +
                    "(3) Xác nhận TenantDomain đúng trong appsettings.json.");

        EntraStepResponse? r;
        try { r = JsonSerializer.Deserialize<EntraStepResponse>(body); }
        catch (JsonException)
        {
            return new("", "", Error: "invalid_response",
                ErrorDescription:
                    $"Entra trả về non-JSON cho '{path}' (HTTP {(int)response.StatusCode}): " +
                    body[..Math.Min(300, body.Length)]);
        }

        if (r is null)
            return new("", "", Error: "null_response",
                ErrorDescription: $"Null deserialization cho '{path}'");

        return new(r.ContinuationToken ?? "", r.ChallengeType ?? "",
            r.CodeLength, r.ChallengeTargetLabel, r.Error, r.ErrorDescription);
    }
}
