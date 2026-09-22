using System.Text.Json;

namespace Udapp.UserCenter;

public sealed class CasdoorOptions
{
    public const string SectionName = "Casdoor";

    public string Authority { get; set; } = "";
    public string Organization { get; set; } = "";
    public string Application { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    /// <summary>本站自己的 Casdoor 应用。注册、开户、本站登录/改资料走它。未传 clientId 的对外登录仍用 <see cref="ClientId"/>。</summary>
    public string SelfClientId { get; set; } = "usercenter";
    /// <summary>Casdoor Application 名称（signupApplication）。空则与 SelfClientId 相同。</summary>
    public string SelfApplication { get; set; } = "";
    public Dictionary<string, string> ClientSecrets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Scope { get; set; } = "openid profile offline_access";

    public string ResolveSelfClientId()
    {
        var id = (SelfClientId ?? "").Trim();
        return string.IsNullOrEmpty(id) ? (ClientId ?? "").Trim() : id;
    }

    public string ResolveSelfApplication()
    {
        var name = (SelfApplication ?? "").Trim();
        return string.IsNullOrEmpty(name) ? ResolveSelfClientId() : name;
    }

    public string SecretFor(string clientId)
    {
        if (ClientSecrets is { Count: > 0 })
        {
            foreach (var kv in ClientSecrets)
            {
                if (string.Equals(kv.Key, clientId, StringComparison.OrdinalIgnoreCase))
                    return kv.Value ?? "";
            }
        }
        return ClientSecret ?? "";
    }
}

/// <summary>
/// 登录/刷新允许的 Casdoor client_id。未传则用默认值；不在名单内则拒绝。
/// </summary>
public sealed class ClientIdGate
{
    public string DefaultId { get; }
    public IReadOnlyList<string> Allowed { get; }

    public ClientIdGate(string defaultId, IEnumerable<string>? allowed, params string[] extra)
    {
        DefaultId = (defaultId ?? "").Trim();
        var list = new List<string>();
        Add(list, DefaultId);
        if (allowed is not null)
        {
            foreach (var item in allowed)
                Add(list, item);
        }
        foreach (var item in extra)
            Add(list, item);
        Allowed = list;
    }

    public bool TryResolve(string? requested, out string clientId)
    {
        var candidate = string.IsNullOrWhiteSpace(requested) ? DefaultId : requested.Trim();
        clientId = "";
        if (string.IsNullOrEmpty(candidate))
            return false;
        var match = Allowed.FirstOrDefault(a => string.Equals(a, candidate, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return false;
        clientId = match;
        return true;
    }

    private static void Add(List<string> list, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        var t = value.Trim();
        if (list.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
            return;
        list.Add(t);
    }
}

/// <summary>
/// 按 clientId 对照 Casdoor JWT roles。名单来自 user-center.config.json 的 ClientApps:{clientId}:allowedRoles。
/// 未配置或空列表不限制；有名单则须有交集（忽略大小写，owner/name 与 name 互通）。管理员不豁免。
/// </summary>
public sealed class ClientRoleGate
{
    private readonly IConfiguration _config;

    public ClientRoleGate(IConfiguration config) => _config = config;

    public bool TryAllow(string clientId, IEnumerable<string>? roleNames)
    {
        var allowed = ReadAllowed(clientId);
        if (allowed.Count == 0)
            return true;
        if (roleNames is null)
            return false;
        foreach (var role in roleNames)
        {
            if (string.IsNullOrWhiteSpace(role))
                continue;
            if (MatchesAny(role, allowed))
                return true;
        }
        return false;
    }

    public IReadOnlyList<string> ReadAllowed(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return [];
        var id = clientId.Trim();
        if (OpsConfigFile.TryReadClientAppRoles(id, out var fromFile))
            return fromFile;
        var list = _config.GetSection($"ClientApps:{id}:allowedRoles").Get<string[]>() ?? [];
        return NormalizeRoles(list);
    }

    internal static IReadOnlyList<string> NormalizeRoles(IEnumerable<string>? raw)
    {
        if (raw is null)
            return [];
        return raw
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesAny(string role, IReadOnlyList<string> allowed)
    {
        var tokens = Tokens(role);
        foreach (var item in allowed)
        {
            foreach (var allowedToken in Tokens(item))
            {
                foreach (var token in tokens)
                {
                    if (string.Equals(allowedToken, token, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        return false;
    }

    private static List<string> Tokens(string raw)
    {
        var list = new List<string>(2);
        var s = raw.Trim();
        if (s.Length == 0)
            return list;
        list.Add(s);
        var slash = s.LastIndexOf('/');
        if (slash >= 0 && slash < s.Length - 1)
        {
            var name = s[(slash + 1)..].Trim();
            if (name.Length > 0 && !list.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                list.Add(name);
        }
        return list;
    }
}

public sealed class ProfileFieldOption
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string BaseUrl { get; set; } = "http://localhost:5010";
    public string Model { get; set; } = "zhipu/glm-4-flash";
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// 对话用的 Chat Hub 规范 id（<c>厂商/模型名</c>）。
    /// 优先运维配置文件 <c>Llm.Model</c>，其次已合并的 <c>Llm:Model</c>，否则启动快照。
    /// </summary>
    public static string ResolveModel(IConfiguration config, LlmOptions fallback)
    {
        var model = (OpsConfigFile.ReadLlmModel() ?? "").Trim();
        if (!string.IsNullOrEmpty(model))
            return model;
        model = (config["Llm:Model"] ?? "").Trim();
        if (!string.IsNullOrEmpty(model))
            return model;
        model = (fallback.Model ?? "").Trim();
        return string.IsNullOrEmpty(model) ? "zhipu/glm-4-flash" : model;
    }

    public string GuideUrl()
    {
        var baseUrl = (BaseUrl ?? "").Trim().TrimEnd('/');
        return string.IsNullOrEmpty(baseUrl) ? "http://localhost:5010/guide" : baseUrl + "/guide";
    }
}

public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    public string BaseUrl { get; set; } = "http://localhost:5111";
    public string Path { get; set; } = "/mcp/sstd";
    public string Token { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 60;
}

public sealed class SqliteOptions
{
    public const string SectionName = "Sqlite";

    public string Directory { get; set; } = @"%APPDATA%\ArchiFrog\UDAppUserCenter\data";
    public string FileName { get; set; } = "user-center.db";

    public string ResolveDbPath()
    {
        var dir = Environment.ExpandEnvironmentVariables(Directory);
        System.IO.Directory.CreateDirectory(dir);
        return Path.Combine(dir, FileName);
    }
}

/// <summary>
/// 运维配置：%AppData%\ArchiFrog\UDAppUserCenter\config\user-center.config.json。
/// Admin = JWT sub ∈ AdminSubs（与 ProtoMass AppServer Auth.AdminSubs 相同）。
/// </summary>
public static class OpsConfigFile
{
    public const string FileName = "user-center.config.json";
    private static readonly object FileLock = new();
    private static string? _llmModel;
    private static Dictionary<string, string[]>? _clientApps;

    public static string DirectoryPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ArchiFrog", "UDAppUserCenter", "config");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string FilePath() => Path.Combine(DirectoryPath(), FileName);

    public static string EnsureExists()
    {
        var path = FilePath();
        if (!File.Exists(path))
        {
            File.WriteAllText(path, DefaultJson());
            return path;
        }

        MergeMissingProvisionKeys(path);
        return path;
    }

    public static (string Username, string Password) ReadProvision(IConfiguration config)
    {
        var username = (config["CasdoorProvision:Username"] ?? "").Trim();
        var password = config["CasdoorProvision:Password"] ?? "";
        return (username, password);
    }

    private static string DefaultJson() => """
        {
          "AdminSubs": [
            "3adf5a14-04f9-4eb8-a497-7543069d594f"
          ],
          "CasdoorProvision": {
            "Username": "ops_admin",
            "Password": ""
          }
        }
        """;

    private static void MergeMissingProvisionKeys(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var hasProvision = root.TryGetProperty("CasdoorProvision", out var provision)
                && provision.ValueKind == JsonValueKind.Object;
            var hasUser = hasProvision && provision.TryGetProperty("Username", out var userEl)
                && userEl.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(userEl.GetString());
            var hasPassword = hasProvision && provision.TryGetProperty("Password", out _);
            if (hasUser && hasPassword) return;

            using var parsed = JsonDocument.Parse(File.ReadAllText(path));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                var wroteProvision = false;
                foreach (var prop in parsed.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals("CasdoorProvision"))
                    {
                        writer.WritePropertyName("CasdoorProvision");
                        writer.WriteStartObject();
                        var username = "ops_admin";
                        var password = "";
                        if (prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            if (prop.Value.TryGetProperty("Username", out var u) && u.ValueKind == JsonValueKind.String)
                                username = string.IsNullOrWhiteSpace(u.GetString()) ? "ops_admin" : u.GetString()!;
                            if (prop.Value.TryGetProperty("Password", out var p) && p.ValueKind == JsonValueKind.String)
                                password = p.GetString() ?? "";
                        }
                        writer.WriteString("Username", username);
                        writer.WriteString("Password", password);
                        writer.WriteEndObject();
                        wroteProvision = true;
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                if (!wroteProvision)
                {
                    writer.WritePropertyName("CasdoorProvision");
                    writer.WriteStartObject();
                    writer.WriteString("Username", "ops_admin");
                    writer.WriteString("Password", "");
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
            }
            File.WriteAllBytes(path, stream.ToArray());
        }
        catch (JsonException)
        {
            // leave file as-is
        }
    }

    public static void WriteLlmModel(string model)
    {
        lock (FileLock)
        {
            var path = EnsureExists();
            using var parsed = JsonDocument.Parse(File.ReadAllText(path));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                var wroteLlm = false;
                foreach (var prop in parsed.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals("Llm"))
                    {
                        WriteLlmObject(writer, prop.Value, model);
                        wroteLlm = true;
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                if (!wroteLlm)
                    WriteLlmObject(writer, default, model);
                writer.WriteEndObject();
            }
            File.WriteAllBytes(path, stream.ToArray());
            _llmModel = model;
        }
    }

    public static bool TryReadClientAppRoles(string clientId, out IReadOnlyList<string> roles)
    {
        roles = [];
        var map = ReadClientApps();
        if (map is null)
            return false;
        foreach (var kv in map)
        {
            if (string.Equals(kv.Key, clientId, StringComparison.OrdinalIgnoreCase))
            {
                roles = ClientRoleGate.NormalizeRoles(kv.Value);
                return true;
            }
        }
        roles = [];
        return true;
    }

    public static IReadOnlyDictionary<string, string[]>? ReadClientApps()
    {
        lock (FileLock)
        {
            try
            {
                var path = FilePath();
                if (!File.Exists(path))
                    return _clientApps;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("ClientApps", out var appsEl))
                {
                    _clientApps = null;
                    return null;
                }
                if (appsEl.ValueKind != JsonValueKind.Object)
                {
                    _clientApps = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                    return _clientApps;
                }
                var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var app in appsEl.EnumerateObject())
                {
                    var id = app.Name.Trim();
                    if (id.Length == 0) continue;
                    string[] roles = [];
                    if (app.Value.ValueKind == JsonValueKind.Object
                        && app.Value.TryGetProperty("allowedRoles", out var rolesEl)
                        && rolesEl.ValueKind == JsonValueKind.Array)
                    {
                        roles = rolesEl.EnumerateArray()
                            .Where(x => x.ValueKind == JsonValueKind.String)
                            .Select(x => (x.GetString() ?? "").Trim())
                            .Where(s => s.Length > 0)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                    }
                    map[id] = roles;
                }
                _clientApps = map;
                return map;
            }
            catch (JsonException)
            {
                return _clientApps;
            }
            catch (IOException)
            {
                return _clientApps;
            }
        }
    }

    public static IReadOnlyDictionary<string, string[]> WriteClientApps(IEnumerable<(string ClientId, IReadOnlyList<string> Roles)> apps)
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (clientId, roles) in apps)
        {
            var id = (clientId ?? "").Trim();
            if (id.Length == 0) continue;
            map[id] = ClientRoleGate.NormalizeRoles(roles).ToArray();
        }

        lock (FileLock)
        {
            var path = EnsureExists();
            using var parsed = JsonDocument.Parse(File.ReadAllText(path));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                var wrote = false;
                foreach (var prop in parsed.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals("ClientApps"))
                    {
                        WriteClientAppsObject(writer, map);
                        wrote = true;
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                if (!wrote)
                    WriteClientAppsObject(writer, map);
                writer.WriteEndObject();
            }
            File.WriteAllBytes(path, stream.ToArray());
            _clientApps = map;
            return map;
        }
    }

    private static void WriteClientAppsObject(Utf8JsonWriter writer, IReadOnlyDictionary<string, string[]> map)
    {
        writer.WritePropertyName("ClientApps");
        writer.WriteStartObject();
        foreach (var kv in map.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            writer.WritePropertyName(kv.Key);
            writer.WriteStartObject();
            writer.WritePropertyName("allowedRoles");
            writer.WriteStartArray();
            foreach (var role in kv.Value)
                writer.WriteStringValue(role);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    public static string? ReadLlmModel()
    {
        lock (FileLock)
        {
            try
            {
                var path = FilePath();
                if (!File.Exists(path))
                    return _llmModel;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("Llm", out var llm)
                    && llm.ValueKind == JsonValueKind.Object
                    && llm.TryGetProperty("Model", out var modelEl)
                    && modelEl.ValueKind == JsonValueKind.String)
                {
                    var value = (modelEl.GetString() ?? "").Trim();
                    if (value.Length > 0)
                    {
                        _llmModel = value;
                        return value;
                    }
                }
            }
            catch (JsonException)
            {
                return _llmModel;
            }
            catch (IOException)
            {
                return _llmModel;
            }
            return _llmModel;
        }
    }

    private static void WriteLlmObject(Utf8JsonWriter writer, JsonElement existing, string model)
    {
        writer.WritePropertyName("Llm");
        writer.WriteStartObject();
        var wroteModel = false;
        if (existing.ValueKind == JsonValueKind.Object)
        {
            foreach (var inner in existing.EnumerateObject())
            {
                if (inner.NameEquals("Model"))
                {
                    writer.WriteString("Model", model);
                    wroteModel = true;
                }
                else
                {
                    inner.WriteTo(writer);
                }
            }
        }
        if (!wroteModel)
            writer.WriteString("Model", model);
        writer.WriteEndObject();
    }

    public static IReadOnlyList<string> ReadAdminSubs(IConfiguration config)
    {
        var list = config.GetSection("AdminSubs").Get<string[]>()
            ?? config.GetSection("Auth:AdminSubs").Get<string[]>()
            ?? [];
        return list
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static bool IsAdmin(IConfiguration config, string? sub)
    {
        if (string.IsNullOrWhiteSpace(sub)) return false;
        var s = sub.Trim();
        return ReadAdminSubs(config).Any(a => string.Equals(a, s, StringComparison.Ordinal));
    }
}
