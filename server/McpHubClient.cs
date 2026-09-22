using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Udapp.UserCenter;

public sealed class McpTool
{
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public JsonNode Parameters { get; init; } = new JsonObject { ["type"] = "object" };
}

public sealed class McpSession : IAsyncDisposable
{
    private readonly McpHubClient _client;
    private readonly string _sessionId;
    private int _nextId = 4;
    private bool _closed;

    internal McpSession(McpHubClient client, string sessionId, IReadOnlyList<McpTool> tools)
    {
        _client = client;
        _sessionId = sessionId;
        Tools = tools;
    }

    public IReadOnlyList<McpTool> Tools { get; }

    public Task<string> CallAsync(string name, JsonNode? arguments, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        return _client.CallToolAsync(_sessionId, id, name, arguments, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_closed) return;
        _closed = true;
        await _client.CloseSessionAsync(_sessionId);
    }
}

public sealed class McpHubClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _http;
    private readonly McpOptions _opt;
    private readonly ILogger<McpHubClient> _log;

    public McpHubClient(IHttpClientFactory http, McpOptions opt, ILogger<McpHubClient> log)
    {
        _http = http;
        _opt = opt;
        _log = log;
    }

    public string Endpoint =>
        _opt.BaseUrl.TrimEnd('/') + "/" + (_opt.Path ?? "/mcp/sstd").Trim().TrimStart('/');

    public async Task<McpSession?> TryOpenAsync(CancellationToken ct)
    {
        try
        {
            return await OpenAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "mcp open failed {Endpoint}", Endpoint);
            return null;
        }
    }

    public async Task<McpSession> OpenAsync(CancellationToken ct)
    {
        var init = await RpcAsync(
            sessionId: null,
            payload: new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "initialize",
                ["params"] = new JsonObject
                {
                    ["protocolVersion"] = "2025-03-26",
                    ["capabilities"] = new JsonObject(),
                    ["clientInfo"] = new JsonObject
                    {
                        ["name"] = "udapp-user-center",
                        ["version"] = "1.0"
                    }
                }
            },
            ct);

        var sessionId = init.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidOperationException("MCP 未返回会话");

        await RpcAsync(
            sessionId,
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/initialized"
            },
            ct,
            expectResult: false);

        var listed = await RpcAsync(
            sessionId,
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 2,
                ["method"] = "tools/list"
            },
            ct);

        var tools = ParseTools(listed.Doc);
        listed.Doc.Dispose();
        init.Doc.Dispose();
        _log.LogInformation("mcp session tools={Count} path={Path}", tools.Count, _opt.Path);
        return new McpSession(this, sessionId, tools);
    }

    internal async Task<string> CallToolAsync(string sessionId, int id, string name, JsonNode? arguments, CancellationToken ct)
    {
        arguments ??= new JsonObject();
        var rpc = await RpcAsync(
            sessionId,
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = "tools/call",
                ["params"] = new JsonObject
                {
                    ["name"] = name,
                    ["arguments"] = arguments.DeepClone()
                }
            },
            ct);
        using (rpc.Doc)
        {
            if (rpc.Doc.RootElement.TryGetProperty("error", out var err))
                return "工具调用失败：" + ReadError(err);

            if (!rpc.Doc.RootElement.TryGetProperty("result", out var result))
                return "工具无返回";

            var isError = result.TryGetProperty("isError", out var flag) && flag.ValueKind == JsonValueKind.True;
            var text = ReadToolContent(result);
            if (string.IsNullOrWhiteSpace(text))
                text = result.GetRawText();
            if (text.Length > 12000)
                text = text[..12000] + "\n…(已截断)";
            return isError ? "工具返回错误：\n" + text : text;
        }
    }

    internal async Task CloseSessionAsync(string sessionId)
    {
        try
        {
            var client = _http.CreateClient("Mcp");
            using var req = new HttpRequestMessage(HttpMethod.Delete, RelativePath());
            ApplyHeaders(req, sessionId);
            using var resp = await client.SendAsync(req, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "mcp session close ignored");
        }
    }

    private async Task<RpcReply> RpcAsync(string? sessionId, JsonObject payload, CancellationToken ct, bool expectResult = true)
    {
        var client = _http.CreateClient("Mcp");
        using var req = new HttpRequestMessage(HttpMethod.Post, RelativePath());
        ApplyHeaders(req, sessionId);
        req.Content = new StringContent(payload.ToJsonString(JsonOpts), Encoding.UTF8, "application/json");

        var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        using (resp)
        {
            var sid = resp.Headers.TryGetValues("mcp-session-id", out var vals)
                ? vals.FirstOrDefault()
                : sessionId;
            sid ??= sessionId;

            if ((int)resp.StatusCode == 202 || resp.Content.Headers.ContentLength == 0)
            {
                if (!expectResult)
                    return new RpcReply(JsonDocument.Parse("{}"), sid ?? "");
            }

            if (!resp.IsSuccessStatusCode && (int)resp.StatusCode != 202)
            {
                var raw = await resp.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException(
                    $"MCP HTTP {(int)resp.StatusCode}: {(raw.Length > 240 ? raw[..240] : raw)}");
            }

            var doc = await ReadJsonRpcAsync(resp, ct);
            if (expectResult && doc.RootElement.TryGetProperty("error", out var err))
            {
                var msg = ReadError(err);
                doc.Dispose();
                throw new InvalidOperationException("MCP: " + msg);
            }
            return new RpcReply(doc, sid ?? "");
        }
    }

    private void ApplyHeaders(HttpRequestMessage req, string? sessionId)
    {
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-03-26");
        if (!string.IsNullOrWhiteSpace(sessionId))
            req.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        if (!string.IsNullOrWhiteSpace(_opt.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.Token);
    }

    private string RelativePath()
    {
        var path = string.IsNullOrWhiteSpace(_opt.Path) ? "/mcp/sstd" : _opt.Path.Trim();
        return path.TrimStart('/');
    }

    private static async Task<JsonDocument> ReadJsonRpcAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var media = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (media.Contains("json", StringComparison.OrdinalIgnoreCase) &&
            !media.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(raw))
                return JsonDocument.Parse("{}");
            return JsonDocument.Parse(raw);
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;
            if (line.Length == 0)
            {
                if (data.Length > 0)
                    break;
                continue;
            }
            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line[5..].Trim());
            }
        }

        var json = data.ToString();
        if (string.IsNullOrWhiteSpace(json))
            return JsonDocument.Parse("{}");
        return JsonDocument.Parse(json);
    }

    private static List<McpTool> ParseTools(JsonDocument doc)
    {
        var list = new List<McpTool>();
        if (!doc.RootElement.TryGetProperty("result", out var result))
            return list;
        if (!result.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var tool in tools.EnumerateArray())
        {
            var name = tool.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;
            var desc = tool.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            JsonNode parameters = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
            if (tool.TryGetProperty("inputSchema", out var schema) && schema.ValueKind == JsonValueKind.Object)
            {
                var parsed = JsonNode.Parse(schema.GetRawText());
                if (parsed is not null) parameters = parsed;
            }
            list.Add(new McpTool { Name = name, Description = desc, Parameters = parameters });
        }
        return list;
    }

    private static string ReadToolContent(JsonElement result)
    {
        if (!result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return "";
        var sb = new StringBuilder();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object) continue;
            if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(text.GetString());
            }
        }
        return sb.ToString();
    }

    private static string ReadError(JsonElement err)
    {
        if (err.ValueKind == JsonValueKind.String)
            return err.GetString() ?? "error";
        if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var msg))
            return msg.GetString() ?? err.GetRawText();
        return err.GetRawText();
    }

    private readonly record struct RpcReply(JsonDocument Doc, string SessionId);
}
