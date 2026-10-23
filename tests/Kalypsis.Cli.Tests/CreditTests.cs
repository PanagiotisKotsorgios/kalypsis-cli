using System.Text.Json;
using Kalypsis.Cli.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kalypsis.Cli.Tests;

public sealed class CreditTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetFullPath(Environment.GetEnvironmentVariable("KALYPSIS_CLI_TEST_ROOT") ?? Path.GetTempPath()), "credits-" + Guid.NewGuid().ToString("N"));
    private readonly Database db;
    private readonly Backoffice office;
    private readonly Row policy;
    public CreditTests()
    {
        db = new(Path.Combine(root, "office.db")); office = new(db, new DateOnly(2026, 10, 23)); policy = Policy("EUR");
    }
    private static Dictionary<string, string> Fields(params (string Key, string Value)[] pairs) => pairs.ToDictionary(x => x.Key, x => x.Value);
    private Row Policy(string currency)
    {
        var customer = office.Add("customers", Fields(("first-name", "Δοκιμή"), ("last-name", currency)));
        var insurer = office.Add("insurers", Fields(("name", "Εταιρεία " + currency)));
        return office.Add("policies", Fields(("number", "POL-" + currency), ("customer", customer.Text("code")), ("insurer", insurer.Text("code")),
            ("start", "2026-01-01"), ("end", "2026-12-31"), ("gross", "100"), ("net", "80"), ("office-rate", "20"), ("currency", currency)));
    }
    private Row Credit(string amount = "20", string? net = null, string? reference = null)
    {
        var fields = Fields(("policy", reference ?? policy.Text("code")), ("amount", amount), ("reason", "Μείωση ασφαλίστρων"));
        if (net is not null) fields["net"] = net;
        return office.Add("credits", fields);
    }

    [Fact]
    public void Partial_credit_reduces_charge_and_commissions_and_never_creates_a_receipt()
    {
        var credit = Credit(); Assert.Equal(16m, credit.Number("net")); Assert.StartsWith("CR-", credit.Text("code"));
        Assert.Equal("EUR", credit.Text("currency")); Assert.Empty(office.List("receipts", Fields()));
        var row = office.Show("policies", policy.Text("code"));
        Assert.Equal(100m, row.Number("gross")); Assert.Equal(20m, row.Number("credited"));
        Assert.Equal(80m, row.Number("chargeable")); Assert.Equal(80m, row.Number("outstanding"));
        Assert.Equal(12.8m, row.Number("office_expected"));
        office.Add("receipts", Fields(("policy", policy.Text("code")), ("amount", "80")));
        Assert.Throws<UserError>(() => office.Add("receipts", Fields(("policy", policy.Text("code")), ("amount", ".01"))));
        Assert.Equal(12.8m, office.Show("policies", policy.Text("code")).Number("office_received"));
    }

    [Fact]
    public void Paid_policy_credit_creates_customer_balance_not_a_fictitious_refund_and_can_be_voided()
    {
        office.Add("receipts", Fields(("policy", policy.Text("code")), ("amount", "100")));
        var credit = Credit(); var row = office.Show("policies", policy.Text("code"));
        Assert.Equal(0m, row.Number("outstanding")); Assert.Equal(20m, row.Number("customer_credit"));
        var summary = Assert.Single(office.Summary()); Assert.Equal(20m, summary.Number("customer_credit"));
        Assert.Equal(20m, summary.Number("credited")); Assert.Equal(100m, summary.Number("received"));
        Assert.Single(office.List("receipts", Fields()));
        office.Update("policies", policy.Text("code"), Fields(("branch", "Κατοικία")));
        var reversed = office.VoidCredit(credit.Text("code"), "Λάθος καταχώρηση");
        Assert.Equal("void", reversed.Text("status")); Assert.Equal("Μείωση ασφαλίστρων", reversed.Text("reason"));
        Assert.Equal("Λάθος καταχώρηση", reversed.Text("void_reason"));
        row = office.Show("policies", policy.Text("code"));
        Assert.Equal(0m, row.Number("customer_credit")); Assert.Equal(16m, row.Number("office_received"));
        Assert.Throws<UserError>(() => office.VoidCredit(credit.Text("code"), "Δεύτερη ακύρωση"));
        var history = office.History("credits", credit.Text("code")); Assert.Equal(2, history.Count);
        Assert.Contains("void", history.Select(x => x.Text("action")));
    }

    [Theory]
    [InlineData("101", null)] [InlineData("0", null)] [InlineData("-1", null)] [InlineData("0.001", null)]
    [InlineData("20", "21")] [InlineData("30", "0")] [InlineData("20", "-1")]
    public void Invalid_credit_rolls_back_without_audit_or_amount_changes(string amount, string? net)
    {
        var events = Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM events"));
        Assert.Throws<UserError>(() => Credit(amount, net));
        Assert.Empty(office.List("credits", Fields()));
        Assert.Equal(events, Convert.ToInt64(db.Scalar("SELECT COUNT(*) FROM events")));
        Assert.Equal(100m, office.Show("policies", policy.Text("code")).Number("outstanding"));
    }

    [Fact]
    public void Final_credit_reconciles_rounding_and_prevents_over_crediting()
    {
        Credit("33.33"); Credit("33.33"); var final = Credit("33.34");
        Assert.Equal(26.68m, final.Number("net"));
        var row = office.Show("policies", policy.Text("code"));
        Assert.Equal(80m, row.Number("credited_net")); Assert.Equal(0m, row.Number("chargeable"));
        Assert.Equal(0m, row.Number("office_received")); Assert.Equal(0m, row.Number("office_expected"));
        Assert.Throws<UserError>(() => Credit(".01"));
    }

    [Fact]
    public void History_guards_cover_posted_and_void_credits_and_related_policy_edits()
    {
        var credit = Credit();
        Assert.Throws<UserError>(() => office.Delete("policies", policy.Text("code")));
        Assert.Throws<UserError>(() => office.Delete("customers", policy.Text("customer_id")));
        Assert.Throws<UserError>(() => office.Delete("credits", credit.Text("code")));
        Assert.Throws<UserError>(() => office.Update("credits", credit.Text("code"), Fields(("amount", "10"))));
        Assert.Throws<UserError>(() => office.Update("policies", policy.Text("code"), Fields(("gross", "10"), ("net", "8"))));
        Assert.Throws<UserError>(() => office.Update("policies", policy.Text("code"), Fields(("currency", "USD"))));
        office.VoidCredit(credit.Text("code"), "Διόρθωση");
        Assert.Throws<UserError>(() => office.Delete("policies", policy.Text("code")));
    }

    [Fact]
    public void Credits_on_cancelled_policies_and_separate_currencies_remain_distinct()
    {
        office.CancelPolicy(policy.Text("code"), "Ακύρωση πελάτη"); Credit();
        var usd = Policy("USD"); Credit("30", reference: usd.Text("code"));
        var summary = office.Summary(); Assert.Equal(2, summary.Count);
        Assert.Equal(20m, summary.Single(x => x.Text("currency") == "EUR").Number("credited"));
        Assert.Equal(30m, summary.Single(x => x.Text("currency") == "USD").Number("credited"));
    }

    [Fact]
    public void Credit_filter_export_json_and_backup_keep_real_values_and_audit()
    {
        var credit = Credit(); var usd = Policy("USD"); Credit("25", reference: usd.Text("code"));
        using var output = new StringWriter(); using var error = new StringWriter();
        Assert.Equal(0, Application.Run(["credits", "list", "--policy", policy.Text("code"), "--db", db.Path, "--json"], new StringReader(""), output, error));
        var records = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("records");
        Assert.Equal(1, records.GetArrayLength()); Assert.Equal(credit.Text("code"), records[0].GetProperty("code").GetString());
        Assert.DoesNotContain("\u001b", output.ToString(), StringComparison.Ordinal);
        var csv = Path.Combine(root, "credits.csv"); Output.Csv(csv, office.List("credits", Fields(("policy", policy.Text("code"))), true));
        Assert.Equal(2, File.ReadAllLines(csv).Length); Assert.Contains(credit.Text("code"), File.ReadAllText(csv));
        var backup = Path.Combine(root, "backup.db"); db.Backup(backup);
        using var restored = new Database(backup); var restoredOffice = new Backoffice(restored);
        Assert.Equal(2, restoredOffice.List("credits", Fields()).Count);
        Assert.Equal(20m, restoredOffice.Show("policies", policy.Text("code")).Number("credited"));
    }

    [Fact]
    public void Existing_v1_database_is_backed_up_and_upgraded_transactionally_without_losing_records()
    {
        var path = Path.Combine(root, "legacy.db");
        using (var initial = new Database(path)) new Backoffice(initial).Add("insurers", Fields(("name", "Legacy insurer")));
        using (var raw = new SqliteConnection("Data Source=" + path))
        {
            raw.Open(); using var command = raw.CreateCommand();
            command.CommandText = "DROP VIEW credit_records; DROP VIEW policy_records; DROP TABLE credits; CREATE VIEW policy_records AS SELECT p.*,c.name AS customer,i.name AS insurer,a.name AS partner,COALESCE((SELECT SUM(r.amount_cents) FROM receipts r WHERE r.policy_id=p.id AND r.status='posted'),0) AS received_cents FROM policies p JOIN customers c ON c.id=p.customer_id JOIN insurers i ON i.id=p.insurer_id LEFT JOIN partners a ON a.id=p.partner_id; PRAGMA user_version=1;";
            command.ExecuteNonQuery();
        }
        using (var upgraded = new Database(path))
        { Assert.Equal(2L, Convert.ToInt64(upgraded.Scalar("PRAGMA user_version"))); Assert.Single(new Backoffice(upgraded).List("insurers", Fields())); }
        var saved = Assert.Single(Directory.GetFiles(root, "legacy.db.schema-v1-*.db"));
        using var original = new SqliteConnection("Data Source=" + saved); original.Open();
        using var check = original.CreateCommand(); check.CommandText = "PRAGMA user_version"; Assert.Equal(1L, Convert.ToInt64(check.ExecuteScalar()));
    }

    public void Dispose()
    {
        db.Dispose(); SqliteConnection.ClearAllPools();
        var parent = Path.GetFullPath(Environment.GetEnvironmentVariable("KALYPSIS_CLI_TEST_ROOT") ?? Path.GetTempPath());
        if (!Path.GetFullPath(root).StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
        Directory.Delete(root, true);
    }
}
