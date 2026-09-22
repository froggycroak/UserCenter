using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace Udapp.UserCenter;

public sealed class ProfileStore
{
    private readonly string _dbPath;
    private readonly IReadOnlyList<ProfileFieldOption> _fields;

    public ProfileStore(SqliteOptions sqlite, IReadOnlyList<ProfileFieldOption> fields)
    {
        _dbPath = sqlite.ResolveDbPath();
        _fields = fields;
        Init();
    }

    public IReadOnlyList<ProfileFieldOption> Fields => _fields;

    public Dictionary<string, string> EmptyFields()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in _fields)
            d[f.Key] = "";
        return d;
    }

    public Dictionary<string, string> GetOrCreate(string sub, string casdoorId, string loginName)
    {
        using var conn = Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT fields_json, login_name FROM user_profiles WHERE sub = $sub";
            cmd.Parameters.AddWithValue("$sub", sub);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var fields = ParseFields(reader.GetString(0));
                var storedName = reader.GetString(1);
                reader.Close();
                if (!string.Equals(storedName, loginName, StringComparison.Ordinal) ||
                    NeedSyncId(conn, sub, casdoorId))
                {
                    TouchIdentity(conn, sub, casdoorId, loginName);
                }
                return fields;
            }
        }

        var empty = EmptyFields();
        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO user_profiles (sub, casdoor_id, login_name, fields_json, created_at, updated_at)
                VALUES ($sub, $id, $name, $json, $now, $now)
                """;
            insert.Parameters.AddWithValue("$sub", sub);
            insert.Parameters.AddWithValue("$id", casdoorId);
            insert.Parameters.AddWithValue("$name", loginName);
            insert.Parameters.AddWithValue("$json", JsonSerializer.Serialize(empty));
            insert.Parameters.AddWithValue("$now", Now());
            insert.ExecuteNonQuery();
        }
        return empty;
    }

    public Dictionary<string, string> UpdateFields(string sub, IReadOnlyDictionary<string, string?> incoming)
    {
        using var conn = Open();
        Dictionary<string, string> current;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT fields_json FROM user_profiles WHERE sub = $sub";
            cmd.Parameters.AddWithValue("$sub", sub);
            var raw = cmd.ExecuteScalar() as string;
            if (raw is null)
                throw new InvalidOperationException("档案不存在");
            current = ParseFields(raw);
        }

        foreach (var field in _fields)
        {
            if (incoming.TryGetValue(field.Key, out var value) && value is not null)
                current[field.Key] = value.Trim();
        }

        using (var upd = conn.CreateCommand())
        {
            upd.CommandText = """
                UPDATE user_profiles
                SET fields_json = $json, updated_at = $now
                WHERE sub = $sub
                """;
            upd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(current));
            upd.Parameters.AddWithValue("$now", Now());
            upd.Parameters.AddWithValue("$sub", sub);
            upd.ExecuteNonQuery();
        }
        return current;
    }

    private Dictionary<string, string> ParseFields(string json)
    {
        var result = EmptyFields();
        if (string.IsNullOrWhiteSpace(json))
            return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var field in _fields)
            {
                if (doc.RootElement.TryGetProperty(field.Key, out var el) && el.ValueKind == JsonValueKind.String)
                    result[field.Key] = el.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
            // keep defaults
        }
        return result;
    }

    private static bool NeedSyncId(Microsoft.Data.Sqlite.SqliteConnection conn, string sub, string casdoorId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT casdoor_id FROM user_profiles WHERE sub = $sub";
        cmd.Parameters.AddWithValue("$sub", sub);
        var stored = cmd.ExecuteScalar() as string;
        return !string.Equals(stored, casdoorId, StringComparison.Ordinal);
    }

    private static void TouchIdentity(Microsoft.Data.Sqlite.SqliteConnection conn, string sub, string casdoorId, string loginName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE user_profiles
            SET casdoor_id = $id, login_name = $name, updated_at = $now
            WHERE sub = $sub
            """;
        cmd.Parameters.AddWithValue("$id", casdoorId);
        cmd.Parameters.AddWithValue("$name", loginName);
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.Parameters.AddWithValue("$sub", sub);
        cmd.ExecuteNonQuery();
    }

    private void Init()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
            cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS user_profiles (
              sub TEXT PRIMARY KEY,
              casdoor_id TEXT NOT NULL,
              login_name TEXT NOT NULL,
              fields_json TEXT NOT NULL DEFAULT '{}',
              created_at TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
        EnsureColumn(conn, "user_profiles", "invite_code", "TEXT");
    }

    public void BindInvite(string sub, string casdoorId, string loginName, string inviteCode)
    {
        GetOrCreate(sub, casdoorId, loginName);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE user_profiles
            SET invite_code = $code, updated_at = $now
            WHERE sub = $sub
            """;
        cmd.Parameters.AddWithValue("$code", inviteCode);
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.Parameters.AddWithValue("$sub", sub);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureColumn(Microsoft.Data.Sqlite.SqliteConnection conn, string table, string column, string type)
    {
        using var info = conn.CreateCommand();
        info.CommandText = $"PRAGMA table_info({table})";
        using var reader = info.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }
        reader.Close();
        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
        alter.ExecuteNonQuery();
    }

    private Microsoft.Data.Sqlite.SqliteConnection Open()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private static string Now() => DateTimeOffset.Now.ToString("o");
}

public sealed class LoginCodeStore
{
    private readonly ConcurrentDictionary<string, CodeEntry> _codes = new();

    public string Issue(string accessToken, string refreshToken, int expiresIn, string clientId)
    {
        Sweep();
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _codes[code] = new CodeEntry(accessToken, refreshToken, expiresIn, clientId, DateTimeOffset.UtcNow.AddSeconds(60));
        return code;
    }

    public CodeEntry? Redeem(string code)
    {
        Sweep();
        if (!_codes.TryRemove(code, out var entry))
            return null;
        if (entry.ExpiresAt < DateTimeOffset.UtcNow)
            return null;
        return entry;
    }

    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _codes)
        {
            if (kv.Value.ExpiresAt < now)
                _codes.TryRemove(kv.Key, out _);
        }
    }

    public sealed record CodeEntry(string AccessToken, string RefreshToken, int ExpiresIn, string ClientId, DateTimeOffset ExpiresAt);
}
