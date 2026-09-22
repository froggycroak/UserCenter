namespace Udapp.UserCenter;

public sealed class SuggestionStore
{
    private readonly string _dbPath;

    public SuggestionStore(SqliteOptions sqlite)
    {
        _dbPath = sqlite.ResolveDbPath();
        Init();
    }

    public SuggestionRow Create(string sub, string account, string productTag, string productOther, string content)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = Now();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO suggestions (id, sub, account, product_tag, product_other, content, created_at)
            VALUES ($id, $sub, $account, $tag, $other, $content, $now)
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$sub", sub);
        cmd.Parameters.AddWithValue("$account", account);
        cmd.Parameters.AddWithValue("$tag", productTag);
        cmd.Parameters.AddWithValue("$other", productOther);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
        return new SuggestionRow(id, sub, account, productTag, productOther, content, now);
    }

    public IReadOnlyList<SuggestionRow> List(int limit = 200)
    {
        if (limit < 1) limit = 1;
        if (limit > 500) limit = 500;
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, sub, account, product_tag, product_other, content, created_at
            FROM suggestions
            ORDER BY created_at DESC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        var rows = new List<SuggestionRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new SuggestionRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? "" : reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6)));
        }
        return rows;
    }

    private void Init()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS suggestions (
              id TEXT PRIMARY KEY,
              sub TEXT NOT NULL,
              account TEXT NOT NULL DEFAULT '',
              product_tag TEXT NOT NULL,
              product_other TEXT NOT NULL DEFAULT '',
              content TEXT NOT NULL,
              created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_suggestions_created_at ON suggestions(created_at DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    private Microsoft.Data.Sqlite.SqliteConnection Open()
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private static string Now() => DateTimeOffset.Now.ToString("o");
}

public sealed record SuggestionRow(
    string Id,
    string Sub,
    string Account,
    string ProductTag,
    string ProductOther,
    string Content,
    string CreatedAt);
