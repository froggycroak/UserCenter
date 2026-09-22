using System.Security.Cryptography;

namespace Udapp.UserCenter;

public sealed class InviteStore
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private readonly string _dbPath;

    public InviteStore(SqliteOptions sqlite)
    {
        _dbPath = sqlite.ResolveDbPath();
        Init();
    }

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var chars = raw.Where(c => c is not ' ' and not '-' and not '_').ToArray();
        return new string(chars).ToUpperInvariant();
    }

    public static string Display(string code)
    {
        if (code.Length == 8)
            return code[..4] + "-" + code[4..];
        return code;
    }

    public IReadOnlyList<InviteRow> List()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT code, created_at, created_by_sub, used_at, used_by_sub, used_by_account, revoked_at
            FROM invite_codes
            ORDER BY created_at DESC
            """;
        var rows = new List<InviteRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new InviteRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                reader.IsDBNull(5) ? "" : reader.GetString(5),
                reader.IsDBNull(6) ? "" : reader.GetString(6)));
        }
        return rows;
    }

    public IReadOnlyList<string> Issue(int count, string createdBySub)
    {
        if (count < 1) count = 1;
        if (count > 50) count = 50;
        var now = Now();
        var created = new List<string>(count);
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        for (var i = 0; i < count; i++)
        {
            string code;
            var attempts = 0;
            while (true)
            {
                code = NewCode();
                attempts++;
                using var insert = conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO invite_codes (code, created_at, created_by_sub)
                    VALUES ($code, $now, $by)
                    """;
                insert.Parameters.AddWithValue("$code", code);
                insert.Parameters.AddWithValue("$now", now);
                insert.Parameters.AddWithValue("$by", string.IsNullOrEmpty(createdBySub) ? DBNull.Value : createdBySub);
                try
                {
                    insert.ExecuteNonQuery();
                    created.Add(code);
                    break;
                }
                catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    if (attempts > 12) throw;
                }
            }
        }
        tx.Commit();
        return created;
    }

    public bool Occupy(string code)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE invite_codes
            SET used_at = $now
            WHERE code = $code AND used_at IS NULL AND revoked_at IS NULL
            """;
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.Parameters.AddWithValue("$code", code);
        return cmd.ExecuteNonQuery() == 1;
    }

    public void Release(string code)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE invite_codes
            SET used_at = NULL, used_by_sub = NULL, used_by_account = NULL
            WHERE code = $code AND revoked_at IS NULL AND used_by_sub IS NULL
            """;
        cmd.Parameters.AddWithValue("$code", code);
        cmd.ExecuteNonQuery();
    }

    public void Complete(string code, string usedBySub, string usedByAccount)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE invite_codes
            SET used_by_sub = $sub, used_by_account = $account, used_at = COALESCE(used_at, $now)
            WHERE code = $code
            """;
        cmd.Parameters.AddWithValue("$sub", usedBySub);
        cmd.Parameters.AddWithValue("$account", usedByAccount);
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.Parameters.AddWithValue("$code", code);
        cmd.ExecuteNonQuery();
    }

    public bool Revoke(string code)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE invite_codes
            SET revoked_at = $now
            WHERE code = $code AND used_at IS NULL AND revoked_at IS NULL
            """;
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.Parameters.AddWithValue("$code", code);
        return cmd.ExecuteNonQuery() == 1;
    }

    private void Init()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS invite_codes (
              code TEXT PRIMARY KEY,
              created_at TEXT NOT NULL,
              created_by_sub TEXT,
              used_at TEXT,
              used_by_sub TEXT,
              used_by_account TEXT,
              revoked_at TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private Microsoft.Data.Sqlite.SqliteConnection Open()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private static string NewCode()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[8];
        for (var i = 0; i < 8; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }

    private static string Now() => DateTimeOffset.Now.ToString("o");
}

public sealed record InviteRow(
    string Code,
    string CreatedAt,
    string CreatedBySub,
    string UsedAt,
    string UsedBySub,
    string UsedByAccount,
    string RevokedAt)
{
    public string Status =>
        !string.IsNullOrEmpty(RevokedAt) ? "revoked"
        : !string.IsNullOrEmpty(UsedBySub) || !string.IsNullOrEmpty(UsedAt) ? "used"
        : "unused";
}
