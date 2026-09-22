using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Udapp.UserCenter;

public sealed class CasdoorClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly CasdoorOptions _opt;
    private readonly ILogger<CasdoorClient> _log;

    public CasdoorClient(HttpClient http, CasdoorOptions opt, ILogger<CasdoorClient> log)
    {
        _http = http;
        _opt = opt;
        _log = log;
        _http.BaseAddress = new Uri(opt.Authority.TrimEnd('/') + "/");
    }

    public async Task<TokenResult> PasswordGrantAsync(
        string username,
        string password,
        CancellationToken ct,
        string? clientId = null)
    {
        var id = string.IsNullOrWhiteSpace(clientId) ? _opt.ClientId : clientId.Trim();
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = id,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = _opt.Scope
        };
        var secret = _opt.SecretFor(id);
        if (!string.IsNullOrEmpty(secret))
            form["client_secret"] = secret;

        return await PostTokenAsync(form, ct);
    }

    public async Task<TokenResult> RefreshAsync(string refreshToken, CancellationToken ct, string? clientId = null)
    {
        var id = string.IsNullOrWhiteSpace(clientId) ? _opt.ClientId : clientId.Trim();
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = id,
            ["refresh_token"] = refreshToken
        };
        var secret = _opt.SecretFor(id);
        if (!string.IsNullOrEmpty(secret))
            form["client_secret"] = secret;

        return await PostTokenAsync(form, ct);
    }

    public async Task<CasdoorStatusResult> SetPasswordAsync(
        string accessToken,
        string userOwner,
        string userName,
        string oldPassword,
        string newPassword,
        CancellationToken ct)
    {
        var url = "api/set-password?" + Uri.EscapeDataString("accessToken") + "=" + Uri.EscapeDataString(accessToken);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["userOwner"] = userOwner,
            ["userName"] = userName,
            ["oldPassword"] = oldPassword,
            ["newPassword"] = newPassword
        });
        using var resp = await _http.PostAsync(url, content, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        CasdoorStatusResult? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<CasdoorStatusResult>(raw, JsonOpts);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Casdoor set-password returned non-JSON");
        }

        return parsed ?? new CasdoorStatusResult
        {
            Status = "error",
            Msg = string.IsNullOrWhiteSpace(raw) ? $"HTTP {(int)resp.StatusCode}" : raw
        };
    }

    public async Task<AddUserResult> AddUserAsync(string adminAccessToken, string userName, string password, CancellationToken ct)
    {
        var payload = new
        {
            owner = _opt.Organization,
            name = userName,
            createdTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            password,
            signupApplication = _opt.ResolveSelfApplication(),
            type = "normal-user"
        };
        var url = "api/add-user?" + Uri.EscapeDataString("accessToken") + "=" + Uri.EscapeDataString(adminAccessToken);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };
        using var resp = await _http.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        CasdoorAddUserResponse? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<CasdoorAddUserResponse>(raw, JsonOpts);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Casdoor add-user returned non-JSON");
        }

        if (parsed is null)
        {
            return AddUserResult.Fail(string.IsNullOrWhiteSpace(raw) ? $"HTTP {(int)resp.StatusCode}" : raw);
        }

        if (!string.Equals(parsed.Status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            var msg = string.IsNullOrWhiteSpace(parsed.Msg) ? "Casdoor 未能创建用户" : parsed.Msg;
            return AddUserResult.Fail(MapAddUserError(msg));
        }

        var id = "";
        var name = userName;
        if (parsed.Data is JsonElement data)
        {
            if (data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    id = idEl.GetString() ?? "";
                if (data.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    name = nameEl.GetString() ?? userName;
            }
            else if (data.ValueKind == JsonValueKind.String)
            {
                var s = data.GetString() ?? "";
                if (s.Contains("already", StringComparison.OrdinalIgnoreCase)
                    || s.Contains("exist", StringComparison.OrdinalIgnoreCase))
                    return AddUserResult.Fail("账号已存在");
            }
        }

        return AddUserResult.Ok(id, name);
    }

    private static string MapAddUserError(string msg)
    {
        if (msg.Contains("exist", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("already", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("重复", StringComparison.Ordinal))
            return "账号已存在";
        if (msg.Contains("password", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Password", StringComparison.Ordinal))
            return "密码不符合要求（至少 6 位）";
        return msg;
    }

    private async Task<TokenResult> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var resp = await _http.PostAsync("api/login/oauth/access_token", content, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        TokenResponse? body;
        try
        {
            body = JsonSerializer.Deserialize<TokenResponse>(raw, JsonOpts);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Casdoor token endpoint returned non-JSON");
            return TokenResult.Fail("Casdoor 响应无法解析");
        }

        if (body is null)
            return TokenResult.Fail("Casdoor 响应为空");

        if (!string.IsNullOrEmpty(body.Error))
            return TokenResult.Fail(body.ErrorDescription ?? body.Error);

        if (string.IsNullOrEmpty(body.AccessToken))
            return TokenResult.Fail("Casdoor 未返回 access_token");

        return TokenResult.Ok(body.AccessToken, body.RefreshToken ?? "", body.ExpiresIn);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }

    private sealed class CasdoorAddUserResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("msg")]
        public string? Msg { get; set; }

        [JsonPropertyName("data")]
        public JsonElement Data { get; set; }
    }
}

public sealed class AddUserResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    public static AddUserResult Ok(string id, string name) =>
        new() { Succeeded = true, Id = id, Name = name };

    public static AddUserResult Fail(string error) =>
        new() { Succeeded = false, Error = error };
}

public sealed class TokenResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public string AccessToken { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public int ExpiresIn { get; init; }

    public static TokenResult Ok(string access, string refresh, int expiresIn) =>
        new() { Succeeded = true, AccessToken = access, RefreshToken = refresh, ExpiresIn = expiresIn };

    public static TokenResult Fail(string error) =>
        new() { Succeeded = false, Error = error };
}

public sealed class CasdoorStatusResult
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    public bool Ok => string.Equals(Status, "ok", StringComparison.OrdinalIgnoreCase);
}
