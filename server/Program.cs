using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Udapp.UserCenter;

var contentRoot = AppContext.BaseDirectory;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot,
    WebRootPath = Path.Combine(contentRoot, "wwwroot")
});
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://0.0.0.0:1010");

var opsConfigPath = OpsConfigFile.EnsureExists();
builder.Configuration.AddJsonFile(opsConfigPath, optional: true, reloadOnChange: true);

var casdoorOpt = builder.Configuration.GetSection(CasdoorOptions.SectionName).Get<CasdoorOptions>()
    ?? throw new InvalidOperationException("Missing Casdoor config");
var sqliteOpt = builder.Configuration.GetSection(SqliteOptions.SectionName).Get<SqliteOptions>()
    ?? new SqliteOptions();
var profileFields = builder.Configuration.GetSection("ProfileFields").Get<List<ProfileFieldOption>>()
    ?? [];
var clientIds = new ClientIdGate(
    casdoorOpt.ClientId,
    builder.Configuration.GetSection("AllowedClientIds").Get<string[]>(),
    casdoorOpt.ResolveSelfClientId());
var audiences = (builder.Configuration.GetSection("JwtAudiences").Get<string[]>() ?? [])
    .Concat(clientIds.Allowed)
    .Where(s => !string.IsNullOrWhiteSpace(s))
    .Select(s => s.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
if (audiences.Length == 0)
    audiences = ["protomassbrowser"];
var corsOrigins = builder.Configuration.GetSection("CorsOrigins").Get<string[]>() ?? [];
var returnAllow = builder.Configuration.GetSection("ReturnUrlAllowList").Get<string[]>() ?? [];
var llmOpt = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
if (string.IsNullOrWhiteSpace(llmOpt.BaseUrl))
    llmOpt.BaseUrl = "http://localhost:5010";
if (string.IsNullOrWhiteSpace(llmOpt.Model))
    llmOpt.Model = "zhipu/glm-4-flash";
if (llmOpt.TimeoutSeconds < 30)
    llmOpt.TimeoutSeconds = 120;
var mcpOpt = builder.Configuration.GetSection(McpOptions.SectionName).Get<McpOptions>() ?? new McpOptions();
if (string.IsNullOrWhiteSpace(mcpOpt.BaseUrl))
    mcpOpt.BaseUrl = "http://localhost:5111";
if (string.IsNullOrWhiteSpace(mcpOpt.Path))
    mcpOpt.Path = "/mcp/sstd";
if (mcpOpt.TimeoutSeconds < 10)
    mcpOpt.TimeoutSeconds = 60;

builder.Services.AddSingleton(casdoorOpt);
builder.Services.AddSingleton(clientIds);
builder.Services.AddSingleton<ClientRoleGate>();
builder.Services.AddSingleton(sqliteOpt);
builder.Services.AddSingleton<IReadOnlyList<ProfileFieldOption>>(profileFields);
builder.Services.AddSingleton<ProfileStore>();
builder.Services.AddSingleton<InviteStore>();
builder.Services.AddSingleton<SuggestionStore>();
builder.Services.AddSingleton<LoginCodeStore>();
builder.Services.AddSingleton(llmOpt);
builder.Services.AddSingleton(mcpOpt);
builder.Services.AddSingleton<McpHubClient>();
builder.Services.AddSingleton<AssistantAgent>();
builder.Services.AddHttpClient<CasdoorClient>();
builder.Services.AddHttpClient("Llm", client =>
{
    client.BaseAddress = new Uri(llmOpt.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(llmOpt.TimeoutSeconds + 10);
});
builder.Services.AddHttpClient("Mcp", client =>
{
    client.BaseAddress = new Uri(mcpOpt.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(mcpOpt.TimeoutSeconds + 10);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = casdoorOpt.Authority;
        options.MetadataAddress = casdoorOpt.Authority.TrimEnd('/') + "/.well-known/openid-configuration";
        options.RequireHttpsMetadata = false;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = casdoorOpt.Authority,
            ValidateAudience = true,
            ValidAudiences = audiences,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            NameClaimType = "name",
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("""{"error":"未登录"}""");
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    p.WithOrigins(corsOrigins.Length == 0 ? ["http://localhost:5173"] : corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod();
}));

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Json(new
{
    ok = true,
    defaultClientId = clientIds.DefaultId,
    selfClientId = casdoorOpt.ResolveSelfClientId()
}));

app.MapGet("/api/me/schema", () =>
{
    var store = app.Services.GetRequiredService<ProfileStore>();
    return Results.Json(new
    {
        fields = store.Fields.Select(f => new { key = f.Key, label = f.Label })
    });
});

app.MapPost("/api/auth/login", async (
    LoginRequest body,
    CasdoorClient casdoor,
    ClientIdGate clientIds,
    ClientRoleGate roleGate,
    LoginCodeStore codes,
    ILoggerFactory logs,
    CancellationToken ct) =>
{
    var log = logs.CreateLogger("Auth");
    var username = (body.Username ?? "").Trim();
    if (username.Length == 0 || string.IsNullOrEmpty(body.Password))
        return Results.Json(new { error = "请输入账号和密码" }, statusCode: 400);
    if (!clientIds.TryResolve(body.ClientId, out var clientId))
        return Results.Json(new { error = "不支持的 clientId" }, statusCode: 400);

    var token = await casdoor.PasswordGrantAsync(username, body.Password, ct, clientId);
    if (!token.Succeeded)
    {
        log.LogInformation("login failed for {User} client={ClientId}", username, clientId);
        return Results.Json(new { error = token.Error ?? "登录失败" }, statusCode: 401);
    }

    var principal = JwtPeek.Read(token.AccessToken);
    if (!roleGate.TryAllow(clientId, principal?.Roles))
    {
        log.LogInformation(
            "login denied roles user={User} client={ClientId} roles={Roles}",
            principal?.Name ?? username,
            clientId,
            string.Join(",", principal?.Roles ?? []));
        return Results.Json(new { error = "您的账户未开通本应用，请联系管理员" }, statusCode: StatusCodes.Status403Forbidden);
    }

    log.LogInformation("login ok {User} sub={Sub} client={ClientId}", principal?.Name ?? username, principal?.Sub, clientId);

    string? redirectTo = null;
    if (TryAllowReturnUrl(body.ReturnUrl, returnAllow, out var returnUrl))
    {
        var code = codes.Issue(token.AccessToken, token.RefreshToken, token.ExpiresIn, clientId);
        redirectTo = AppendQuery(returnUrl, "code", code);
    }

    return Results.Json(new
    {
        accessToken = token.AccessToken,
        refreshToken = token.RefreshToken,
        expiresIn = token.ExpiresIn,
        clientId,
        sub = principal?.Sub,
        id = principal?.Id,
        name = principal?.Name,
        account = principal?.Name,
        owner = principal?.Owner,
        redirectTo
    });
});

app.MapPost("/api/auth/refresh", async (
    RefreshRequest body,
    CasdoorClient casdoor,
    ClientIdGate clientIds,
    ClientRoleGate roleGate,
    ILoggerFactory logs,
    CancellationToken ct) =>
{
    var log = logs.CreateLogger("Auth");
    if (string.IsNullOrWhiteSpace(body.RefreshToken))
        return Results.Json(new { error = "缺少 refreshToken" }, statusCode: 400);
    if (!clientIds.TryResolve(body.ClientId, out var clientId))
        return Results.Json(new { error = "不支持的 clientId" }, statusCode: 400);

    var token = await casdoor.RefreshAsync(body.RefreshToken, ct, clientId);
    if (!token.Succeeded)
        return Results.Json(new { error = token.Error ?? "刷新失败" }, statusCode: 401);

    var principal = JwtPeek.Read(token.AccessToken);
    if (!roleGate.TryAllow(clientId, principal?.Roles))
    {
        log.LogInformation(
            "refresh denied roles user={User} client={ClientId} roles={Roles}",
            principal?.Name ?? "",
            clientId,
            string.Join(",", principal?.Roles ?? []));
        return Results.Json(new { error = "您的账户未开通本应用，请联系管理员" }, statusCode: StatusCodes.Status403Forbidden);
    }

    return Results.Json(new
    {
        accessToken = token.AccessToken,
        refreshToken = token.RefreshToken,
        expiresIn = token.ExpiresIn,
        clientId,
        sub = principal?.Sub,
        id = principal?.Id,
        name = principal?.Name,
        account = principal?.Name
    });
});

app.MapPost("/api/auth/exchange", (ExchangeRequest body, LoginCodeStore codes) =>
{
    if (string.IsNullOrWhiteSpace(body.Code))
        return Results.Json(new { error = "缺少 code" }, statusCode: 400);
    var entry = codes.Redeem(body.Code);
    if (entry is null)
        return Results.Json(new { error = "code 无效或已过期" }, statusCode: 400);
    var principal = JwtPeek.Read(entry.AccessToken);
    return Results.Json(new
    {
        accessToken = entry.AccessToken,
        refreshToken = entry.RefreshToken,
        expiresIn = entry.ExpiresIn,
        clientId = entry.ClientId,
        sub = principal?.Sub,
        id = principal?.Id,
        name = principal?.Name,
        account = principal?.Name
    });
});

app.MapPost("/api/auth/logout", () => Results.Json(new { ok = true }));

app.MapPost("/api/auth/register", async (
    RegisterRequest body,
    CasdoorClient casdoor,
    InviteStore invites,
    ProfileStore profiles,
    IConfiguration config,
    ILoggerFactory logs,
    CancellationToken ct) =>
{
    var log = logs.CreateLogger("Register");
    var username = (body.Username ?? "").Trim();
    var password = body.Password ?? "";
    var inviteCode = InviteStore.Normalize(body.InviteCode);
    if (username.Length == 0 || password.Length == 0 || inviteCode.Length == 0)
        return Results.Json(new { error = "请输入账号、密码和邀请码" }, statusCode: 400);
    if (username.Contains('/') || username.Contains('\\') || username.Length > 64)
        return Results.Json(new { error = "账号格式不正确" }, statusCode: 400);
    if (password.Length < 6)
        return Results.Json(new { error = "密码至少 6 位" }, statusCode: 400);

    var (provisionUser, provisionPassword) = OpsConfigFile.ReadProvision(config);
    if (string.IsNullOrEmpty(provisionUser) || string.IsNullOrEmpty(provisionPassword))
        return Results.Json(new { error = "注册暂不可用：未配置开户账号" }, statusCode: 503);

    if (!invites.Occupy(inviteCode))
        return Results.Json(new { error = "邀请码无效或已使用" }, statusCode: 400);

    try
    {
        var selfClientId = casdoorOpt.ResolveSelfClientId();
        var adminToken = await casdoor.PasswordGrantAsync(provisionUser, provisionPassword, ct, selfClientId);
        if (!adminToken.Succeeded)
        {
            invites.Release(inviteCode);
            log.LogWarning("provision login failed client={ClientId}: {Error}", selfClientId, adminToken.Error);
            return Results.Json(new { error = "注册暂不可用" }, statusCode: 503);
        }

        var created = await casdoor.AddUserAsync(adminToken.AccessToken, username, password, ct);
        if (!created.Succeeded)
        {
            invites.Release(inviteCode);
            log.LogInformation("register failed for {User}: {Error}", username, created.Error);
            return Results.Json(new { error = created.Error ?? "注册失败" }, statusCode: 400);
        }

        var sub = created.Id;
        var account = string.IsNullOrEmpty(created.Name) ? username : created.Name;
        if (string.IsNullOrEmpty(sub))
        {
            var probe = await casdoor.PasswordGrantAsync(username, password, ct, selfClientId);
            if (probe.Succeeded)
                sub = JwtPeek.Read(probe.AccessToken)?.Sub ?? "";
        }
        if (string.IsNullOrEmpty(sub))
        {
            log.LogWarning("register created Casdoor user {User} but id is empty", username);
            invites.Complete(inviteCode, "", account);
            return Results.Json(new { ok = true });
        }

        profiles.BindInvite(sub, sub, account, inviteCode);
        invites.Complete(inviteCode, sub, account);
        log.LogInformation("register ok {User} sub={Sub}", account, sub);
        return Results.Json(new { ok = true });
    }
    catch (Exception ex)
    {
        invites.Release(inviteCode);
        log.LogError(ex, "register exception for {User}", username);
        return Results.Json(new { error = "注册失败" }, statusCode: 500);
    }
});

app.MapGet("/api/auth/me", (ClaimsPrincipal user, ProfileStore store) =>
{
    var me = CasdoorUser.From(user);
    if (me is null) return Results.Unauthorized();
    var fields = store.GetOrCreate(me.Sub, me.Id, me.Name);
    return Results.Json(new
    {
        sub = me.Sub,
        id = me.Id,
        account = me.Name,
        name = me.Name,
        owner = me.Owner,
        profile = fields
    });
}).RequireAuthorization();

app.MapGet("/api/me/profile", (ClaimsPrincipal user, ProfileStore store) =>
{
    var me = CasdoorUser.From(user);
    if (me is null) return Results.Unauthorized();
    var fields = store.GetOrCreate(me.Sub, me.Id, me.Name);
    return Results.Json(new
    {
        sub = me.Sub,
        id = me.Id,
        account = me.Name,
        owner = me.Owner,
        fields,
        schema = store.Fields.Select(f => new { key = f.Key, label = f.Label })
    });
}).RequireAuthorization();

app.MapPut("/api/me/profile", async (HttpRequest req, ClaimsPrincipal user, ProfileStore store, CancellationToken ct) =>
{
    var me = CasdoorUser.From(user);
    if (me is null) return Results.Unauthorized();
    store.GetOrCreate(me.Sub, me.Id, me.Name);

    using var doc = await JsonDocument.ParseAsync(req.Body, cancellationToken: ct);
    var incoming = new Dictionary<string, string?>(StringComparer.Ordinal);
    if (doc.RootElement.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Object)
    {
        foreach (var p in fieldsEl.EnumerateObject())
            incoming[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
    }
    else
    {
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Name is "sub" or "id" or "account" or "name" or "owner")
                continue;
            incoming[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
        }
    }

    var updated = store.UpdateFields(me.Sub, incoming);
    return Results.Json(new { fields = updated });
}).RequireAuthorization();

app.MapGet("/api/ops/me", (ClaimsPrincipal user, IConfiguration config) =>
{
    var me = CasdoorUser.From(user);
    if (me is null) return Results.Unauthorized();
    var isAdmin = OpsConfigFile.IsAdmin(config, me.Sub);
    var payload = new
    {
        sub = me.Sub,
        id = me.Id,
        account = me.Name,
        name = me.Name,
        isAdmin
    };
    if (!isAdmin)
    {
        return Results.Json(new
        {
            error = "需要管理员权限",
            payload.sub,
            payload.id,
            payload.account,
            payload.name,
            payload.isAdmin
        }, statusCode: StatusCodes.Status403Forbidden);
    }
    return Results.Json(payload);
}).RequireAuthorization();

app.MapGet("/api/ops/invites", (ClaimsPrincipal user, IConfiguration config, InviteStore invites) =>
{
    var denied = RequireOpsAdmin(user, config, out _);
    if (denied is not null) return denied;
    var rows = invites.List().Select(r => new
    {
        code = r.Code,
        display = InviteStore.Display(r.Code),
        status = r.Status,
        createdAt = r.CreatedAt,
        createdBySub = r.CreatedBySub,
        usedAt = r.UsedAt,
        usedBySub = r.UsedBySub,
        usedByAccount = r.UsedByAccount,
        revokedAt = r.RevokedAt
    });
    return Results.Json(new { invites = rows });
}).RequireAuthorization();

app.MapPost("/api/ops/invites", (InviteIssueRequest body, ClaimsPrincipal user, IConfiguration config, InviteStore invites) =>
{
    var denied = RequireOpsAdmin(user, config, out var me);
    if (denied is not null) return denied;
    var count = body.Count is > 0 and <= 50 ? body.Count.Value : 10;
    var codes = invites.Issue(count, me!.Sub);
    return Results.Json(new
    {
        codes = codes.Select(c => new { code = c, display = InviteStore.Display(c) })
    });
}).RequireAuthorization();

app.MapPost("/api/suggestions", async (
    HttpRequest req,
    ClaimsPrincipal user,
    SuggestionStore suggestions,
    CancellationToken ct) =>
{
    var me = CasdoorUser.From(user);
    if (me is null) return Results.Unauthorized();

    SuggestionRequest? body;
    try
    {
        body = await req.ReadFromJsonAsync<SuggestionRequest>(cancellationToken: ct);
    }
    catch (JsonException)
    {
        return Results.Json(new { error = "请求格式无效" }, statusCode: StatusCodes.Status400BadRequest);
    }

    var content = (body?.Content ?? "").Trim();
    var productTag = (body?.ProductTag ?? "").Trim();
    var productOther = (body?.ProductOther ?? "").Trim();

    if (string.IsNullOrEmpty(productTag))
        return Results.Json(new { error = "请选择产品标签" }, statusCode: StatusCodes.Status400BadRequest);
    if (productTag.Length > 64)
        return Results.Json(new { error = "产品标签过长" }, statusCode: StatusCodes.Status400BadRequest);
    if (string.IsNullOrEmpty(content))
        return Results.Json(new { error = "请填写建议内容" }, statusCode: StatusCodes.Status400BadRequest);
    if (content.Length > 4000)
        return Results.Json(new { error = "建议内容过长" }, statusCode: StatusCodes.Status400BadRequest);
    productOther = "";

    var row = suggestions.Create(me.Sub, me.Name, productTag, productOther, content);
    return Results.Json(new
    {
        ok = true,
        id = row.Id,
        createdAt = row.CreatedAt
    });
}).RequireAuthorization();

app.MapGet("/api/ops/suggestions", (ClaimsPrincipal user, IConfiguration config, SuggestionStore suggestions) =>
{
    var denied = RequireOpsAdmin(user, config, out _);
    if (denied is not null) return denied;
    var rows = suggestions.List().Select(r => new
    {
        id = r.Id,
        sub = r.Sub,
        account = r.Account,
        productTag = r.ProductTag,
        productOther = r.ProductOther,
        content = r.Content,
        createdAt = r.CreatedAt
    });
    return Results.Json(new { suggestions = rows });
}).RequireAuthorization();

app.MapGet("/api/ops/client-apps", (
    ClaimsPrincipal user,
    IConfiguration config,
    ClientIdGate clientIds,
    ClientRoleGate roleGate) =>
{
    var denied = RequireOpsAdmin(user, config, out _);
    if (denied is not null) return denied;

    var known = new List<string>();
    foreach (var id in clientIds.Allowed)
        AddClientId(known, id);

    var stored = OpsConfigFile.ReadClientApps();
    if (stored is not null)
    {
        foreach (var id in stored.Keys)
            AddClientId(known, id);
    }

    var apps = known.Select(id => new
    {
        clientId = id,
        allowedRoles = roleGate.ReadAllowed(id)
    });
    return Results.Json(new { apps });
}).RequireAuthorization();

app.MapPost("/api/ops/client-apps", (
    ClientAppsRequest body,
    ClaimsPrincipal user,
    IConfiguration config) =>
{
    var denied = RequireOpsAdmin(user, config, out _);
    if (denied is not null) return denied;

    var incoming = new List<(string ClientId, IReadOnlyList<string> Roles)>();
    foreach (var item in body.Apps ?? [])
    {
        var id = (item.ClientId ?? "").Trim();
        if (id.Length == 0)
            continue;
        if (id.Length > 64)
            return Results.Json(new { error = "clientId 过长" }, statusCode: StatusCodes.Status400BadRequest);
        if (!id.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'))
            return Results.Json(new { error = $"clientId 无效：{id}" }, statusCode: StatusCodes.Status400BadRequest);
        var roles = ClientRoleGate.NormalizeRoles(item.AllowedRoles);
        if (roles.Count > 32)
            return Results.Json(new { error = $"{id} 的角色过多" }, statusCode: StatusCodes.Status400BadRequest);
        if (roles.Any(r => r.Length > 64))
            return Results.Json(new { error = $"{id} 的角色名过长" }, statusCode: StatusCodes.Status400BadRequest);
        incoming.Add((id, roles));
    }

    try
    {
        var saved = OpsConfigFile.WriteClientApps(incoming);
        return Results.Json(new
        {
            ok = true,
            apps = saved.Select(kv => new { clientId = kv.Key, allowedRoles = kv.Value })
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = "写入运维配置失败：" + ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
}).RequireAuthorization();

app.MapGet("/api/ops/llm", async (
    ClaimsPrincipal user,
    IConfiguration config,
    LlmOptions llm,
    IHttpClientFactory http,
    CancellationToken ct) =>
{
    var denied = RequireOpsAdmin(user, config, out _);
    if (denied is not null) return denied;

    var model = LlmOptions.ResolveModel(config, llm);
    string? hubDefault = null;
    string? fetchError = null;
    var models = new List<object>();
    try
    {
        var client = http.CreateClient("Llm");
        using var resp = await client.GetAsync("v1/models", ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            fetchError = AssistantAgent.ExtractLlmError(raw) ?? $"无法读取模型名单 ({(int)resp.StatusCode})";
        }
        else
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("default", out var defEl) && defEl.ValueKind == JsonValueKind.String)
                hubDefault = defEl.GetString();
            if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataEl.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                        ? (idEl.GetString() ?? "").Trim()
                        : "";
                    if (id.Length == 0) continue;
                    var ownedBy = "";
                    if (item.TryGetProperty("owned_by", out var byEl) && byEl.ValueKind == JsonValueKind.String)
                        ownedBy = (byEl.GetString() ?? "").Trim();
                    if (ownedBy.Length == 0)
                    {
                        var slash = id.IndexOf('/');
                        ownedBy = slash > 0 ? id[..slash] : "other";
                    }
                    models.Add(new { id, ownedBy });
                }
            }
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        fetchError = "无法连接 Chat Hub：" + ex.Message;
    }

    return Results.Json(new
    {
        baseUrl = llm.BaseUrl,
        guideUrl = llm.GuideUrl(),
        model,
        hubDefault,
        models,
        error = fetchError
    });
}).RequireAuthorization();

app.MapPost("/api/ops/llm", (
    LlmModelRequest body,
    ClaimsPrincipal user,
    IConfiguration config,
    LlmOptions llm) =>
{
    var denied = RequireOpsAdmin(user, config, out _);
    if (denied is not null) return denied;
    var model = (body.Model ?? "").Trim();
    if (model.Length == 0)
        return Results.Json(new { error = "请选择模型" }, statusCode: StatusCodes.Status400BadRequest);
    if (model.Length > 128)
        return Results.Json(new { error = "模型 id 过长" }, statusCode: StatusCodes.Status400BadRequest);
    try
    {
        OpsConfigFile.WriteLlmModel(model);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = "写入运维配置失败：" + ex.Message }, statusCode: StatusCodes.Status500InternalServerError);
    }
    return Results.Json(new
    {
        ok = true,
        model,
        baseUrl = llm.BaseUrl,
        guideUrl = llm.GuideUrl()
    });
}).RequireAuthorization();

app.MapPost("/api/ops/invites/{code}/revoke", (string code, ClaimsPrincipal user, IConfiguration config, InviteStore invites) =>
{
    var denied = RequireOpsAdmin(user, config, out _);
    if (denied is not null) return denied;
    var normalized = InviteStore.Normalize(code);
    if (normalized.Length == 0)
        return Results.Json(new { error = "邀请码无效" }, statusCode: 400);
    if (!invites.Revoke(normalized))
        return Results.Json(new { error = "无法作废（不存在、已使用或已作废）" }, statusCode: 400);
    return Results.Json(new { ok = true });
}).RequireAuthorization();

app.MapPost("/api/me/password", async (
    PasswordRequest body,
    ClaimsPrincipal user,
    HttpRequest req,
    CasdoorClient casdoor,
    CancellationToken ct) =>
{
    var me = CasdoorUser.From(user);
    if (me is null) return Results.Unauthorized();
    if (string.IsNullOrEmpty(body.OldPassword) || string.IsNullOrEmpty(body.NewPassword))
        return Results.Json(new { error = "请输入旧密码和新密码" }, statusCode: 400);
    if (body.NewPassword.Length < 6)
        return Results.Json(new { error = "新密码至少 6 位" }, statusCode: 400);

    var accessToken = BearerToken(req);
    if (string.IsNullOrEmpty(accessToken))
        return Results.Unauthorized();

    var result = await casdoor.SetPasswordAsync(accessToken, me.Owner, me.Name, body.OldPassword, body.NewPassword, ct);
    if (!result.Ok)
        return Results.Json(new { error = result.Msg ?? "改密失败" }, statusCode: 400);
    return Results.Json(new { ok = true });
}).RequireAuthorization();

app.MapPost("/api/assistant/chat", async (
    AssistantChatRequest body,
    HttpContext ctx,
    ClaimsPrincipal user,
    AssistantAgent assistant,
    IConfiguration config,
    LlmOptions llm,
    ILoggerFactory logs,
    CancellationToken ct) =>
{
    var me = CasdoorUser.From(user);
    if (me is null)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { error = "未登录" }, cancellationToken: ct);
        return;
    }

    var cleaned = new List<(string Role, string Content)>();
    foreach (var item in body.Messages ?? [])
    {
        var role = (item.Role ?? "").Trim().ToLowerInvariant();
        var content = (item.Content ?? "").Trim();
        if (role is not ("user" or "assistant") || content.Length == 0)
            continue;
        if (content.Length > 8000)
            content = content[..8000];
        cleaned.Add((role, content));
        if (cleaned.Count >= 40)
            break;
    }
    if (cleaned.Count == 0)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { error = "请输入消息" }, cancellationToken: ct);
        return;
    }

    var log = logs.CreateLogger("Assistant");
    log.LogInformation("chat sub={Sub} messages={Count} model={Model}", me.Sub, cleaned.Count, LlmOptions.ResolveModel(config, llm));
    await assistant.WriteChatAsync(ctx, cleaned, me.Sub, ct);
}).RequireAuthorization();

if (!string.IsNullOrEmpty(app.Environment.WebRootPath) && Directory.Exists(app.Environment.WebRootPath))
    app.MapFallbackToFile("index.html");

var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
startupLog.LogInformation("Ops config: {Path}", opsConfigPath);
startupLog.LogInformation("Ops gate: /__ops  AdminSubs: {Count}", OpsConfigFile.ReadAdminSubs(app.Configuration).Count);
var provision = OpsConfigFile.ReadProvision(app.Configuration);
startupLog.LogInformation("Casdoor provision user: {User} configured={Configured}",
    string.IsNullOrEmpty(provision.Username) ? "(unset)" : provision.Username,
    !string.IsNullOrEmpty(provision.Password));
startupLog.LogInformation("Casdoor clientIds: default={Default} self={Self} allowed={Allowed}",
    clientIds.DefaultId, casdoorOpt.ResolveSelfClientId(), string.Join(",", clientIds.Allowed));

startupLog.LogInformation("LLM: {Base} model={Model}", llmOpt.BaseUrl, LlmOptions.ResolveModel(app.Configuration, llmOpt));
startupLog.LogInformation("MCP: {Endpoint}", app.Services.GetRequiredService<McpHubClient>().Endpoint);
app.Run();

static string? BearerToken(HttpRequest req)
{
    var header = req.Headers.Authorization.ToString();
    const string prefix = "Bearer ";
    if (header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        return header[prefix.Length..].Trim();
    return null;
}

static bool TryAllowReturnUrl(string? raw, string[] allow, out string url)
{
    url = "";
    if (string.IsNullOrWhiteSpace(raw) || allow.Length == 0)
        return false;
    if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        return false;
    if (uri.Scheme is not ("http" or "https"))
        return false;
    foreach (var prefix in allow)
    {
        if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            url = raw;
            return true;
        }
    }
    return false;
}

static string AppendQuery(string url, string key, string value)
{
    var sep = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
    return url + sep + Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value);
}

static IResult? RequireOpsAdmin(ClaimsPrincipal user, IConfiguration config, out CasdoorUser? me)
{
    me = CasdoorUser.From(user);
    if (me is null) return Results.Unauthorized();
    if (!OpsConfigFile.IsAdmin(config, me.Sub))
    {
        return Results.Json(new
        {
            error = "需要管理员权限",
            sub = me.Sub,
            id = me.Id,
            account = me.Name,
            name = me.Name,
            isAdmin = false
        }, statusCode: StatusCodes.Status403Forbidden);
    }
    return null;
}

static void AddClientId(List<string> list, string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return;
    var t = value.Trim();
    if (list.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
        return;
    list.Add(t);
}

internal sealed record LoginRequest(string? Username, string? Password, string? ReturnUrl, string? ClientId);
internal sealed record RegisterRequest(string? Username, string? Password, string? InviteCode);
internal sealed record RefreshRequest(string? RefreshToken, string? ClientId);
internal sealed record ExchangeRequest(string? Code);
internal sealed record PasswordRequest(string? OldPassword, string? NewPassword);
internal sealed record InviteIssueRequest(int? Count);
internal sealed record SuggestionRequest(string? ProductTag, string? ProductOther, string? Content);
internal sealed record LlmModelRequest(string? Model);
internal sealed record ClientAppRolesRequest(string? ClientId, List<string>? AllowedRoles);
internal sealed record ClientAppsRequest(List<ClientAppRolesRequest>? Apps);
internal sealed record AssistantChatRequest(List<AssistantChatMessage>? Messages);
internal sealed record AssistantChatMessage(string? Role, string? Content);

internal sealed record CasdoorUser(string Sub, string Id, string Name, string Owner)
{
    public IReadOnlyList<string> Roles { get; init; } = [];

    public static CasdoorUser? From(ClaimsPrincipal user)
    {
        var sub = user.FindFirst("sub")?.Value;
        var name = user.FindFirst("name")?.Value;
        if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(name))
            return null;
        var id = user.FindFirst("id")?.Value;
        if (string.IsNullOrEmpty(id))
            id = sub;
        var owner = user.FindFirst("owner")?.Value ?? "UDAppUser";
        return new CasdoorUser(sub, id, name, owner);
    }
}

internal static class JwtPeek
{
    public static CasdoorUser? Read(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
            return null;
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(Base64Url(parts[1]));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var sub = Get(root, "sub");
            var name = Get(root, "name");
            if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(name))
                return null;
            var id = Get(root, "id");
            if (string.IsNullOrEmpty(id))
                id = sub;
            var owner = Get(root, "owner") ?? "UDAppUser";
            return new CasdoorUser(sub, id, name, owner) { Roles = ReadRoles(root) };
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadRoles(JsonElement root)
    {
        if (!root.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<string>();
        foreach (var el in roles.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    AddRole(list, s);
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                var name = Get(el, "name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                var owner = Get(el, "owner");
                if (!string.IsNullOrWhiteSpace(owner))
                    AddRole(list, owner.Trim() + "/" + name.Trim());
                AddRole(list, name);
            }
        }
        return list;
    }

    private static void AddRole(List<string> list, string value)
    {
        var t = value.Trim();
        if (t.Length == 0)
            return;
        if (list.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
            return;
        list.Add(t);
    }

    private static string? Get(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static byte[] Base64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
