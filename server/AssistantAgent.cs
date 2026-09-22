using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Udapp.UserCenter;

public sealed class AssistantAgent
{
    private const int MaxRounds = 8;
    private const string SystemPrompt = """
        你是用户中心的智能助手。当前已接入华建制度 MCP（/mcp/sstd）。
        使用工具的原则：
        - 用户问华建集团 / 华建科技内部制度、管理办法、技术标准、会议纪要时，必须调用 search_sstd_policy，依据检索结果作答，并注明来源（文件名、页码）。找不到就明确说未检索到，禁止编造文号或条款。
        - 问当前日期或时间时调用 get_datetime；问造价、面积、单价等计算时调用 calc_numbers。
        - 用户给出 http/https 链接并要求阅读或列链接时，使用 fetch_web_text 或 fetch_web_links。
        - 与上述无关时不要调用工具。用中文简洁回答。
        """;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly LlmOptions _llm;
    private readonly McpHubClient _mcp;
    private readonly ILogger<AssistantAgent> _log;

    public AssistantAgent(IHttpClientFactory http, IConfiguration config, LlmOptions llm, McpHubClient mcp, ILogger<AssistantAgent> log)
    {
        _http = http;
        _config = config;
        _llm = llm;
        _mcp = mcp;
        _log = log;
    }

    public async Task WriteChatAsync(HttpContext ctx, IReadOnlyList<(string Role, string Content)> history, string sub, CancellationToken ct)
    {
        await BeginSse(ctx, ct);

        await using var session = await _mcp.TryOpenAsync(ct);
        var mcpTools = session?.Tools ?? [];
        var llmTools = ToLlmTools(mcpTools);

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = SystemPrompt }
        };
        foreach (var (role, content) in history)
            messages.Add(new JsonObject { ["role"] = role, ["content"] = content });

        try
        {
            for (var round = 0; round < MaxRounds; round++)
            {
                ct.ThrowIfCancellationRequested();
                var allowTools = session is not null && llmTools.Count > 0 && round < MaxRounds - 1;
                using var completion = await CompleteAsync(messages, allowTools ? llmTools : null, ct);
                var choice = FirstChoice(completion.RootElement);
                var message = choice.TryGetProperty("message", out var msgEl) ? msgEl : default;
                var finish = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;
                var toolCalls = ReadToolCalls(message);

                if (toolCalls.Count > 0 && allowTools && session is not null)
                {
                    messages.Add(JsonNode.Parse(message.GetRawText())!);
                    foreach (var call in toolCalls)
                    {
                        await WriteSse(ctx, new
                        {
                            uc = new { kind = "tool", name = call.Name, label = ToolLabel(call.Name), status = "start" }
                        }, ct);
                        string result;
                        try
                        {
                            result = await session.CallAsync(call.Name, call.Arguments, ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "mcp tool failed name={Name}", call.Name);
                            result = "工具调用失败：" + ex.Message;
                        }
                        await WriteSse(ctx, new
                        {
                            uc = new { kind = "tool", name = call.Name, label = ToolLabel(call.Name), status = "done" }
                        }, ct);
                        messages.Add(new JsonObject
                        {
                            ["role"] = "tool",
                            ["tool_call_id"] = call.Id,
                            ["name"] = call.Name,
                            ["content"] = result
                        });
                    }
                    _log.LogInformation("chat tools sub={Sub} round={Round} names={Names}",
                        sub, round, string.Join(",", toolCalls.Select(t => t.Name)));
                    continue;
                }

                var text = ReadContent(message);
                if (!string.IsNullOrEmpty(text))
                    await WriteSse(ctx, new { choices = new[] { new { delta = new { content = text } } } }, ct);
                else if (string.Equals(finish, "tool_calls", StringComparison.OrdinalIgnoreCase))
                    await WriteSse(ctx, new { choices = new[] { new { delta = new { content = "工具调用次数已用尽，请再问一次。" } } } }, ct);

                await ctx.Response.WriteAsync("data: [DONE]\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
                return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "assistant agent failed sub={Sub}", sub);
            var msg = ex is InvalidOperationException && !string.IsNullOrWhiteSpace(ex.Message)
                ? ex.Message
                : "助手暂时不可用";
            await WriteSse(ctx, new { error = msg }, CancellationToken.None);
        }
    }

    private async Task<JsonDocument> CompleteAsync(JsonArray messages, JsonArray? tools, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = LlmOptions.ResolveModel(_config, _llm),
            ["messages"] = messages.DeepClone(),
            ["stream"] = false
        };
        if (tools is { Count: > 0 })
            body["tools"] = tools.DeepClone();

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(JsonOpts), Encoding.UTF8, "application/json")
        };
        var client = _http.CreateClient("Llm");
        using var resp = await client.SendAsync(req, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(ExtractLlmError(raw) ?? $"助手请求失败 ({(int)resp.StatusCode})");
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
    }

    private static JsonArray ToLlmTools(IReadOnlyList<McpTool> tools)
    {
        var arr = new JsonArray();
        foreach (var tool in tools)
        {
            arr.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = tool.Parameters.DeepClone()
                }
            });
        }
        return arr;
    }

    private static JsonElement FirstChoice(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0)
            return choices[0];
        throw new InvalidOperationException("模型未返回结果");
    }

    private static List<ToolCall> ReadToolCalls(JsonElement message)
    {
        var list = new List<ToolCall>();
        if (message.ValueKind != JsonValueKind.Object) return list;
        if (!message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var call in calls.EnumerateArray())
        {
            var id = call.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrEmpty(id)) id = "call_" + list.Count;
            string? name = null;
            JsonNode? args = null;
            if (call.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
            {
                name = fn.TryGetProperty("name", out var n) ? n.GetString() : null;
                args = ParseArgs(fn);
            }
            else
            {
                name = call.TryGetProperty("name", out var n) ? n.GetString() : null;
            }
            if (string.IsNullOrWhiteSpace(name)) continue;
            list.Add(new ToolCall(id, name, args ?? new JsonObject()));
        }
        return list;
    }

    private static JsonNode ParseArgs(JsonElement fn)
    {
        if (!fn.TryGetProperty("arguments", out var a))
            return new JsonObject();
        try
        {
            if (a.ValueKind == JsonValueKind.Object)
                return JsonNode.Parse(a.GetRawText()) ?? new JsonObject();
            if (a.ValueKind == JsonValueKind.String)
            {
                var s = a.GetString();
                if (string.IsNullOrWhiteSpace(s)) return new JsonObject();
                return JsonNode.Parse(s) ?? new JsonObject();
            }
        }
        catch (JsonException)
        {
            // ignore
        }
        return new JsonObject();
    }

    private static string? ReadContent(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object) return null;
        if (!message.TryGetProperty("content", out var c)) return null;
        if (c.ValueKind == JsonValueKind.String) return c.GetString();
        if (c.ValueKind != JsonValueKind.Array) return null;
        var sb = new StringBuilder();
        foreach (var part in c.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
                sb.Append(part.GetString());
            else if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var t))
                sb.Append(t.GetString());
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private static async Task BeginSse(HttpContext ctx, CancellationToken ct)
    {
        ctx.Response.StatusCode = StatusCodes.Status200OK;
        ctx.Response.ContentType = "text/event-stream; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";
        ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
        await ctx.Response.WriteAsync(": ok\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }

    private static async Task WriteSse(HttpContext ctx, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        await ctx.Response.WriteAsync("data: " + json + "\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }

    private static string ToolLabel(string name) => name switch
    {
        "search_sstd_policy" => "检索华建制度",
        "get_datetime" => "查询时间",
        "calc_numbers" => "计算数值",
        "fetch_web_text" => "读取网页",
        "fetch_web_links" => "提取链接",
        "hub_health" => "检查工具状态",
        _ => name
    };

    internal static string? ExtractLlmError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("detail", out var detail))
            {
                if (detail.ValueKind == JsonValueKind.String)
                    return detail.GetString();
                return detail.ToString();
            }
            if (root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String)
                    return err.GetString();
                if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var msg))
                    return msg.GetString();
            }
        }
        catch (JsonException)
        {
            // fall through
        }
        return raw.Length > 240 ? raw[..240] : raw;
    }

    private readonly record struct ToolCall(string Id, string Name, JsonNode Arguments);
}
