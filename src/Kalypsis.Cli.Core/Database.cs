using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Kalypsis.Cli.Core;

public sealed class Row : Dictionary<string, object?>
{
    public Row() : base(StringComparer.OrdinalIgnoreCase) { }
    public string Text(string key) => Convert.ToString(this.GetValueOrDefault(key), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public long Id => Convert.ToInt64(this["id"]);
    public decimal Number(string key) => Convert.ToDecimal(this.GetValueOrDefault(key) ?? 0);
}

public sealed class Database : IDisposable
{
    private const int ApplicationId = 1262701641;
    private static readonly HashSet<string> MonetaryFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "amount", "gross", "net", "received", "credited", "credited_net",
        "chargeable", "chargeable_net", "outstanding", "customer_credit",
        "office_expected", "office_received", "partner_expected",
        "credit_note_balance", "overdue_policies_total", "unpaid_receipts_total",
        "total", "balance", "commission", "monthly_amount"
    };
    private readonly SqliteConnection connection;
    private SqliteTransaction? transaction;
    public string Path { get; }

    public Database(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, ForeignKeys = true, Pooling = false, DefaultTimeout = 5 }.ToString());
        connection.Open();
        try
        {
            connection.CreateCollation("NOCASE", (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left, right));
            connection.CreateFunction<string?, string?, bool>("contains_text", (value, search) =>
                System.Globalization.CultureInfo.GetCultureInfo("el-GR").CompareInfo.IndexOf(value ?? "", search ?? "",
                    System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0);
            var appId = Convert.ToInt64(Scalar("PRAGMA application_id"));
            var version = Convert.ToInt64(Scalar("PRAGMA user_version"));
            if (appId != ApplicationId && (appId != 0 || Convert.ToInt64(Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")) > 0))
                throw new UserError("Το αρχείο δεν είναι βάση KALYPSIS CLI. Δεν έγιναν αλλαγές.");
            if (version > 2) throw new UserError("Η βάση δημιουργήθηκε από νεότερη έκδοση. Αναβαθμίστε το CLI.");
            if (appId == ApplicationId && version == 0) throw new UserError("Η βάση δεν έχει έγκυρη έκδοση σχήματος.");
            if (appId == 0)
            {
                Atomic(() => { Execute(Schema); Execute($"PRAGMA application_id={ApplicationId}; PRAGMA user_version=1;"); return 0; });
            }
            if (version < 2)
            {
                if (appId == ApplicationId)
                    Backup(Path + ".schema-v1-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".db");
                Atomic(() => {
                    if (Convert.ToInt64(Scalar("PRAGMA user_version")) < 2)
                    {
                        if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM pragma_table_info('insurers') WHERE name='city'")) == 0)
                            Execute("ALTER TABLE insurers ADD COLUMN city TEXT NOT NULL DEFAULT ''");
                        if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM pragma_table_info('partners') WHERE name='city'")) == 0)
                            Execute("ALTER TABLE partners ADD COLUMN city TEXT NOT NULL DEFAULT ''");
                        Execute(CreditSchema);
                        Execute("PRAGMA user_version=2;");
                    }
                    return 0;
                });
            }
            Execute("PRAGMA journal_mode=WAL;");
        }
        catch { connection.Dispose(); throw; }
    }

    public T Atomic<T>(Func<T> action)
    {
        using var scope = connection.BeginTransaction(); transaction = scope;
        try { var result = action(); scope.Commit(); return result; }
        finally { transaction = null; }
    }

    private SqliteCommand Command(string sql, params (string Key, object? Value)[] parameters)
    {
        var command = connection.CreateCommand(); command.CommandText = sql; command.Transaction = transaction;
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return command;
    }
    public int Execute(string sql, params (string Key, object? Value)[] parameters) { using var command = Command(sql, parameters); return command.ExecuteNonQuery(); }
    public object? Scalar(string sql, params (string Key, object? Value)[] parameters) { using var command = Command(sql, parameters); return command.ExecuteScalar(); }
    public List<Row> Query(string sql, params (string Key, object? Value)[] parameters)
    {
        using var command = Command(sql, parameters); using var reader = command.ExecuteReader(); var rows = new List<Row>();
        while (reader.Read())
        {
            var row = new Row();
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var name = reader.GetName(index); object? value = reader.IsDBNull(index) ? null : reader.GetValue(index);
                if (name.EndsWith("_cents", StringComparison.Ordinal))
                {
                    name = name[..^6];
                    value = value is null ? null : Convert.ToDecimal(value) / 100m;
                }
                else if (name.EndsWith("_bps", StringComparison.Ordinal))
                {
                    name = name == "partner_bps" ? "partner_rate" : name == "office_bps" ? "office_rate" : name[..^4];
                    value = value is null ? null : Convert.ToDecimal(value) / 100m;
                }
                else if (MonetaryFields.Contains(name) && value is not null && value is not string)
                {
                    value = Convert.ToDecimal(value) / 100m;
                }
                row[name] = value;
            }
            rows.Add(row);
        }
        return rows;
    }

    public long Insert(string table, Dictionary<string, object?> values, string prefix)
    {
        var fields = new Dictionary<string, object?>(values) { ["created_at"] = DateTimeOffset.UtcNow.ToString("O") };
        if (!fields.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code?.ToString())) fields["code"] = Guid.NewGuid().ToString("N");
        Execute($"INSERT INTO {table} ({string.Join(',', fields.Keys)}) VALUES ({string.Join(',', fields.Keys.Select(key => "$" + key))})", fields.Select(pair => ("$" + pair.Key, pair.Value)).ToArray());
        var id = Convert.ToInt64(Scalar("SELECT last_insert_rowid()"));
        if (string.IsNullOrWhiteSpace(code?.ToString())) Execute($"UPDATE {table} SET code=$code WHERE id=$id", ("$code", $"{prefix}-{id:000000}"), ("$id", id));
        return id;
    }
    public void Update(string table, long id, Dictionary<string, object?> values)
    {
        if (values.Count == 0) throw new UserError("Δεν δόθηκαν πεδία για αλλαγή.");
        Execute($"UPDATE {table} SET {string.Join(',', values.Keys.Select(key => key + "=$" + key))} WHERE id=$id", values.Select(pair => ("$" + pair.Key, pair.Value)).Append(("$id", (object?)id)).ToArray());
    }
    public void Audit(string entity, long id, string action, Row? before, Row? after) => Execute(
        "INSERT INTO events(entity,entity_id,action,occurred_at,before_json,after_json) VALUES($entity,$id,$action,$date,$before,$after)",
        ("$entity", entity), ("$id", id), ("$action", action), ("$date", DateTimeOffset.UtcNow.ToString("O")),
        ("$before", before is null ? null : JsonSerializer.Serialize(before)), ("$after", after is null ? null : JsonSerializer.Serialize(after)));

    public void Backup(string destination)
    {
        var target = System.IO.Path.GetFullPath(destination);
        if (string.Equals(target, Path, StringComparison.OrdinalIgnoreCase) || File.Exists(target)) throw new UserError("Το αρχείο προορισμού υπάρχει ήδη. Επιλέξτε διαφορετικό όνομα.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        using (new FileStream(target, FileMode.CreateNew, FileAccess.Write)) { }
        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString());
        backup.Open(); connection.BackupDatabase(backup);
    }
    public void Dispose() => connection.Dispose();

    private const string CreditSchema = """
        CREATE TABLE credits(id INTEGER PRIMARY KEY AUTOINCREMENT,code TEXT NOT NULL COLLATE NOCASE UNIQUE,number TEXT NOT NULL UNIQUE,policy_id INTEGER NOT NULL REFERENCES policies(id) ON DELETE RESTRICT,amount_cents INTEGER NOT NULL,net_cents INTEGER NOT NULL,date TEXT NOT NULL,status TEXT NOT NULL DEFAULT 'posted' CHECK(status IN ('posted','void')),reason TEXT NOT NULL,void_reason TEXT,created_at TEXT NOT NULL DEFAULT (datetime('now')));
        CREATE INDEX ix_credit_policy ON credits(policy_id);
        DROP VIEW policy_records;
        CREATE VIEW policy_records AS SELECT p.*,c.name AS customer,i.name AS insurer,a.name AS partner,COALESCE((SELECT SUM(r.amount_cents) FROM receipts r WHERE r.policy_id=p.id AND r.status='posted'),0) AS received,COALESCE((SELECT SUM(cr.amount_cents) FROM credits cr WHERE cr.policy_id=p.id AND cr.status='posted'),0) AS credited,COALESCE((SELECT SUM(cr.net_cents) FROM credits cr WHERE cr.policy_id=p.id AND cr.status='posted'),0) AS credited_net,(p.gross_cents - COALESCE((SELECT SUM(cr.amount_cents) FROM credits cr WHERE cr.policy_id=p.id AND cr.status='posted'),0)) AS chargeable,(p.net_cents - COALESCE((SELECT SUM(cr.net_cents) FROM credits cr WHERE cr.policy_id=p.id AND cr.status='posted'),0)) AS chargeable_net,CASE WHEN p.status='cancelled' THEN 0 ELSE MAX(0, p.gross_cents - COALESCE((SELECT SUM(cr.amount_cents) FROM credits cr WHERE cr.policy_id=p.id AND cr.status='posted'),0) - COALESCE((SELECT SUM(r.amount_cents) FROM receipts r WHERE r.policy_id=p.id AND r.status='posted'),0)) END AS outstanding,CASE WHEN p.status='cancelled' THEN 0 ELSE MAX(0, COALESCE((SELECT SUM(r.amount_cents) FROM receipts r WHERE r.policy_id=p.id AND r.status='posted'),0) - (p.gross_cents - COALESCE((SELECT SUM(cr.amount_cents) FROM credits cr WHERE cr.policy_id=p.id AND cr.status='posted'),0))) END AS customer_credit,p.status,p.created_at FROM policies p JOIN customers c ON c.id=p.customer_id LEFT JOIN insurers i ON i.id=p.insurer_id LEFT JOIN partners a ON a.id=p.partner_id;
        CREATE VIEW credit_records AS SELECT cr.*,p.code AS policy_code,p.number AS policy,c.name AS customer,p.currency FROM credits cr JOIN policies p ON p.id=cr.policy_id JOIN customers c ON c.id=p.customer_id;
        """;

    private const string Schema = """
        CREATE TABLE customers(id INTEGER PRIMARY KEY AUTOINCREMENT,code TEXT NOT NULL COLLATE NOCASE UNIQUE,kind TEXT NOT NULL CHECK(kind IN('person','company')),first_name TEXT NOT NULL DEFAULT '',last_name TEXT NOT NULL DEFAULT '',name TEXT NOT NULL DEFAULT '',vat TEXT NOT NULL DEFAULT '',phone TEXT NOT NULL DEFAULT '',email TEXT NOT NULL DEFAULT '',city TEXT NOT NULL DEFAULT '',created_at TEXT NOT NULL DEFAULT (datetime('now')));
        CREATE TABLE insurers(id INTEGER PRIMARY KEY AUTOINCREMENT,code TEXT NOT NULL COLLATE NOCASE UNIQUE,name TEXT NOT NULL,phone TEXT NOT NULL DEFAULT '',email TEXT NOT NULL DEFAULT '',city TEXT NOT NULL DEFAULT '',created_at TEXT NOT NULL DEFAULT (datetime('now')));
        CREATE TABLE partners(id INTEGER PRIMARY KEY AUTOINCREMENT,code TEXT NOT NULL COLLATE NOCASE UNIQUE,name TEXT NOT NULL,phone TEXT NOT NULL DEFAULT '',email TEXT NOT NULL DEFAULT '',city TEXT NOT NULL DEFAULT '',commission_bps INTEGER NOT NULL DEFAULT 0,created_at TEXT NOT NULL DEFAULT (datetime('now')));
        CREATE TABLE policies(id INTEGER PRIMARY KEY AUTOINCREMENT,code TEXT NOT NULL COLLATE NOCASE UNIQUE,number TEXT NOT NULL,customer_id INTEGER NOT NULL REFERENCES customers(id) ON DELETE RESTRICT,insurer_id INTEGER NOT NULL REFERENCES insurers(id) ON DELETE RESTRICT,partner_id INTEGER REFERENCES partners(id) ON DELETE RESTRICT,previous_id INTEGER REFERENCES policies(id) ON DELETE RESTRICT,branch TEXT NOT NULL DEFAULT '',plate TEXT NOT NULL DEFAULT '',start TEXT NOT NULL,end TEXT NOT NULL,gross_cents INTEGER NOT NULL,net_cents INTEGER NOT NULL,currency TEXT NOT NULL CHECK(currency IN ('EUR','USD','GBP','CHF','SEK','NOK','DKK','JPY','CAD','AUD')),office_bps INTEGER NOT NULL DEFAULT 0,partner_bps INTEGER NOT NULL DEFAULT 0,status TEXT NOT NULL DEFAULT 'active' CHECK(status IN ('active','cancelled','renewed')),created_at TEXT NOT NULL DEFAULT (datetime('now')));
        CREATE TABLE receipts(id INTEGER PRIMARY KEY AUTOINCREMENT,code TEXT NOT NULL COLLATE NOCASE UNIQUE,number TEXT NOT NULL UNIQUE,policy_id INTEGER NOT NULL REFERENCES policies(id) ON DELETE RESTRICT,amount_cents INTEGER NOT NULL,date TEXT NOT NULL,status TEXT NOT NULL DEFAULT 'posted' CHECK(status IN ('posted','void')),method TEXT NOT NULL DEFAULT 'bank' CHECK(method IN ('cash','bank','card','other')),reason TEXT,created_at TEXT NOT NULL DEFAULT (datetime('now')));
        CREATE INDEX ix_policy_customer ON policies(customer_id);
        CREATE INDEX ix_policy_insurer ON policies(insurer_id);
        CREATE INDEX ix_policy_partner ON policies(partner_id);
        CREATE INDEX ix_policy_previous ON policies(previous_id);
        CREATE INDEX ix_receipt_policy ON receipts(policy_id);
        CREATE TABLE events(id INTEGER PRIMARY KEY AUTOINCREMENT,entity TEXT NOT NULL,entity_id INTEGER NOT NULL,action TEXT NOT NULL,occurred_at TEXT NOT NULL,before_json TEXT,after_json TEXT);
        CREATE INDEX ix_event_entity ON events(entity,entity_id);
        CREATE VIEW policy_records AS SELECT p.*,c.name AS customer,i.name AS insurer,a.name AS partner,COALESCE((SELECT SUM(r.amount_cents) FROM receipts r WHERE r.policy_id=p.id AND r.status='posted'),0) AS received,COALESCE((SELECT SUM(cr.amount_cents) FROM credits cr WHERE cr.policy_id=p.id AND cr.status='posted'),0) AS credited,COALESCE((SELECT SUM(cr.net_cents) FROM credits cr WHERE cr.policy_id=p.id AND cr.status='posted'),0) AS credited_net,(p.gross_cents - COALESCE((SELECT SUM(cr.amount_cents) FROM credits cr WHERE cr.policy_id=p.id AND cr.status='posted'),0)) AS chargeable,(p.net_cents - COALESCE((SELECT SUM(cr.net_cents) FROM credits cr WHERE cr.policy_id=p.id AND cr.status='posted'),0)) AS chargeable_net,CASE WHEN p.status='cancelled' THEN 0 ELSE MAX(0, p.gross_cents - COALESCE((SELECT SUM(cr.amount_cents) FROM credits cr WHERE cr.policy_id=p.id AND cr.status='posted'),0) - COALESCE((SELECT SUM(r.amount_cents) FROM receipts r WHERE r.policy_id=p.id AND r.status='posted'),0)) END AS outstanding,CASE WHEN p.status='cancelled' THEN 0 ELSE MAX(0, COALESCE((SELECT SUM(r.amount_cents) FROM receipts r WHERE r.policy_id=p.id AND r.status='posted'),0) - (p.gross_cents - COALESCE((SELECT SUM(cr.amount_cents) FROM credits cr WHERE cr.policy_id=p.id AND cr.status='posted'),0))) END AS customer_credit,p.status,p.created_at FROM policies p JOIN customers c ON c.id=p.customer_id LEFT JOIN insurers i ON i.id=p.insurer_id LEFT JOIN partners a ON a.id=p.partner_id;
        CREATE VIEW receipt_records AS SELECT r.*,p.code AS policy_code,p.number AS policy,c.name AS customer,p.currency FROM receipts r JOIN policies p ON p.id=r.policy_id JOIN customers c ON c.id=p.customer_id;
        """;
}
