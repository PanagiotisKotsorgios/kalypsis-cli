using Kalypsis.Cli.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kalypsis.Cli.Tests;

public sealed class BackofficeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetFullPath(Environment.GetEnvironmentVariable("KALYPSIS_CLI_TEST_ROOT") ?? Path.Combine(Path.GetTempPath(), "kalypsis-cli-tests")), Guid.NewGuid().ToString("N"));
    private readonly Database db;
    private readonly Backoffice office;
    public BackofficeTests() { db = new(Path.Combine(root, "office.db")); office = new(db, new DateOnly(2026, 10, 23)); }
    public void Dispose()
    {
        db.Dispose();
        var parent = Path.GetFullPath(Environment.GetEnvironmentVariable("KALYPSIS_CLI_TEST_ROOT") ?? Path.Combine(Path.GetTempPath(), "kalypsis-cli-tests"));
        if (!Path.GetFullPath(root).StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test cleanup target");
        Directory.Delete(root, true);
    }
    private static Dictionary<string, string> Fields(params (string Key, string Value)[] fields) => fields.ToDictionary(field => field.Key, field => field.Value);
    private Row Customer() => office.Add("customers", Fields(("first-name", "Μαρία"), ("last-name", "Παπαδοπούλου"), ("city", "Αθήνα")));
    private Row Insurer() => office.Add("insurers", Fields(("name", "Ασφαλιστική δοκιμής")));
    private Row Policy(bool partner = false, string currency = "EUR")
    {
        var args = Fields(("number", "POL-" + Guid.NewGuid().ToString("N")[..8]), ("customer", Customer().Text("code")), ("insurer", Insurer().Text("code")),
            ("start", "2026-01-01"), ("end", "2026-12-31"), ("gross", "100"), ("net", "80"), ("office-rate", "20"), ("currency", currency));
        if (partner) args["partner"] = office.Add("partners", Fields(("name", "Συνεργάτης"), ("commission", "10"))).Text("code");
        return office.Add("policies", args);
    }

    [Fact]
    public void Fresh_database_is_empty_persistent_and_identified_as_cli_data()
    {
        Assert.Empty(office.List("customers", Fields()));
        var customer = Customer(); Assert.Equal("C-000001", customer.Text("code"));
        using var reopened = new Database(db.Path);
        Assert.Equal("Μαρία Παπαδοπούλου", new Backoffice(reopened).Show("customers", customer.Text("code")).Text("name"));
    }

    [Fact]
    public void Greek_search_ignores_case_and_accents_and_input_is_parameterised()
    {
        var customer = Customer();
        Assert.Single(office.List("customers", Fields(("search", "μαρια"))));
        Assert.Single(office.List("customers", Fields(("search", "ΑΘΗΝΑ"))));
        Assert.Empty(office.List("customers", Fields(("search", "' OR 1=1 --"))));
        office.Update("customers", customer.Text("code"), Fields(("last-name", "O'Brien")));
        Assert.Equal("Μαρία O'Brien", office.Show("customers", customer.Text("code")).Text("name"));
    }

    [Fact]
    public void Company_customer_does_not_require_personal_names_and_codes_are_unique()
    {
        var row = office.Add("customers", Fields(("kind", "company"), ("name", "Εταιρεία Α.Ε."), ("code", "CUSTOM")));
        Assert.Equal("company", row.Text("kind"));
        Assert.Equal(row.Id, office.Show("customers", "custom").Id);
        Assert.Throws<SqliteException>(() => office.Add("customers", Fields(("kind", "company"), ("name", "Άλλη"), ("code", "custom"))));
        Assert.Single(office.List("customers", Fields()));
    }

    [Theory]
    [InlineData("vat", "1234567890")]
    [InlineData("email", "invalid")]
    [InlineData("phone", "letters")]
    [InlineData("first-name", "")]
    public void Invalid_customer_input_never_creates_half_records(string field, string value)
    {
        var fields = Fields(("first-name", "Test"), ("last-name", "Customer")); fields[field] = value;
        Assert.Throws<UserError>(() => office.Add("customers", fields));
        Assert.Empty(office.List("customers", Fields()));
        Assert.Equal(0L, db.Scalar("SELECT COUNT(*) FROM events"));
    }

    [Fact]
    public void Partial_receipts_recognise_only_the_office_share_collected_and_void_reverses_it()
    {
        var policy = Policy(); Assert.Equal(16m, policy.Number("office_expected")); Assert.Equal(0m, policy.Number("office_received"));
        var receipt = office.Add("receipts", Fields(("policy", policy.Text("code")), ("amount", "50,00"), ("number", "R1")));
        var partial = office.Show("policies", policy.Text("code"));
        Assert.Equal(50m, partial.Number("outstanding")); Assert.Equal(8m, partial.Number("office_received"));
        office.Add("receipts", Fields(("policy", policy.Text("code")), ("amount", "50")));
        Assert.Equal(0m, office.Show("policies", policy.Text("code")).Number("outstanding"));
        Assert.Equal(16m, office.Show("policies", policy.Text("code")).Number("office_received"));
        Assert.Throws<UserError>(() => office.Add("receipts", Fields(("policy", policy.Text("code")), ("amount", "0.01"))));
        office.VoidReceipt(receipt.Text("code"), "Λανθασμένη είσπραξη");
        Assert.Equal(50m, office.Show("policies", policy.Text("code")).Number("outstanding"));
        Assert.Equal(8m, office.Show("policies", policy.Text("code")).Number("office_received"));
        Assert.Throws<UserError>(() => office.VoidReceipt(receipt.Text("code"), "Again"));
        Assert.Equal("void", office.Show("receipts", receipt.Text("code")).Text("status"));
    }

    [Fact]
    public void Duplicate_receipt_numbers_roll_back_without_increasing_received_amount()
    {
        var policy = Policy(); var fields = Fields(("policy", policy.Text("code")), ("amount", "20"), ("number", "DUP"));
        office.Add("receipts", fields);
        Assert.Throws<SqliteException>(() => office.Add("receipts", fields));
        Assert.Equal(20m, office.Show("policies", policy.Text("code")).Number("received"));
        Assert.Single(office.List("receipts", Fields()));
    }

    [Fact]
    public void Linked_records_cannot_be_deleted_including_after_voiding_a_receipt()
    {
        var policy = Policy(true);
        foreach (var (entity, id) in new[] { ("customers", policy.Text("customer_id")), ("insurers", policy.Text("insurer_id")), ("partners", policy.Text("partner_id")) })
            Assert.Throws<UserError>(() => office.Delete(entity, id));
        var receipt = office.Add("receipts", Fields(("policy", policy.Text("code")), ("amount", "1")));
        office.VoidReceipt(receipt.Text("code"), "Error");
        Assert.Throws<UserError>(() => office.Delete("policies", policy.Text("code")));
    }

    [Fact]
    public void Unlinked_customer_can_be_deleted_and_audit_is_preserved()
    {
        var customer = Customer(); office.Delete("customers", customer.Text("code"));
        Assert.Empty(office.List("customers", Fields()));
        Assert.Equal(2L, db.Scalar("SELECT COUNT(*) FROM events WHERE entity='customers'"));
        Assert.Equal(2, office.History("customers", customer.Text("code")).Count);
        Assert.Equal(2, office.History("customers", customer.Text("id")).Count);
    }

    [Fact]
    public void Renewal_is_atomic_preserves_rates_and_links_previous_contract()
    {
        var original = Policy(true);
        var renewal = office.Renew(original.Text("code"), Fields(("number", "NEXT"), ("start", "2027-01-01"), ("end", "2027-12-31")));
        Assert.Equal(original.Id, Convert.ToInt64(renewal["previous_id"]));
        Assert.Equal(8m, renewal.Number("partner_expected")); Assert.Equal(16m, renewal.Number("office_expected"));
        Assert.Equal("renewed", office.Show("policies", original.Text("code")).Text("status"));
        Assert.Throws<UserError>(() => office.Renew(original.Text("code"), Fields(("number", "THIRD"), ("start", "2028-01-01"), ("end", "2028-12-31"))));
        Assert.Throws<UserError>(() => office.Delete("policies", renewal.Text("code")));
    }

    [Fact]
    public void Failed_renewal_does_not_change_old_status()
    {
        var original = Policy();
        Assert.Throws<UserError>(() => office.Renew(original.Text("code"), Fields(("number", "NEXT"), ("start", "2026-05-01"), ("end", "2027-12-31"))));
        Assert.Equal("active", office.Show("policies", original.Text("code")).Text("status"));
        Assert.Single(office.List("policies", Fields()));
    }

    [Fact]
    public void Policy_edit_preserves_partner_rate_and_blocks_money_or_party_corruption()
    {
        var policy = Policy(true);
        var updated = office.Update("policies", policy.Text("code"), Fields(("plate", "ABC1234")));
        Assert.Equal(10m, updated.Number("partner_rate")); Assert.Equal("Συνεργάτης", updated.Text("partner"));
        office.Add("receipts", Fields(("policy", policy.Text("code")), ("amount", "70")));
        Assert.Throws<UserError>(() => office.Update("policies", policy.Text("code"), Fields(("gross", "60"), ("net", "50"))));
        Assert.Throws<UserError>(() => office.Update("policies", policy.Text("code"), Fields(("currency", "USD"))));
        Assert.Throws<UserError>(() => office.Update("policies", policy.Text("code"), Fields(("customer", Customer().Text("code")))));
        Assert.Equal(100m, office.Show("policies", policy.Text("code")).Number("gross"));
    }

    [Fact]
    public void Summary_keeps_currencies_separate_and_cancel_does_not_invent_a_refund()
    {
        var eur = Policy(); var usd = Policy(currency: "USD");
        office.Add("receipts", Fields(("policy", eur.Text("code")), ("amount", "25")));
        office.Add("receipts", Fields(("policy", usd.Text("code")), ("amount", "50")));
        var summary = office.Summary(); Assert.Equal(2, summary.Count);
        Assert.Equal(25m, summary.Single(row => row.Text("currency") == "EUR").Number("received"));
        office.CancelPolicy(eur.Text("code"), "Cancellation");
        Assert.Equal(25m, office.Show("policies", eur.Text("code")).Number("received"));
        Assert.Throws<UserError>(() => office.Add("receipts", Fields(("policy", eur.Text("code")), ("amount", "1"))));
    }

    [Fact]
    public void Voided_financial_history_still_blocks_changing_the_original_customer()
    {
        var policy = Policy();
        var receipt = office.Add("receipts", Fields(("policy", policy.Text("code")), ("amount", "1")));
        office.VoidReceipt(receipt.Text("code"), "Wrong entry");
        Assert.Equal(0m, office.Show("policies", policy.Text("code")).Number("received"));
        Assert.Throws<UserError>(() => office.Update("policies", policy.Text("code"), Fields(("customer", Customer().Text("code")))));
    }

    [Fact]
    public void List_limit_does_not_limit_filtered_export_results()
    {
        Customer(); Customer(); Customer();
        var filter = Fields(("search", "Μαρία"), ("limit", "1"));
        Assert.Single(office.List("customers", filter));
        Assert.Equal(3, office.List("customers", filter, true).Count);
    }

    [Fact]
    public void Numeric_custom_codes_never_make_ID_lookup_ambiguous()
    {
        var first = Customer();
        office.Add("customers", Fields(("kind", "company"), ("name", "Second"), ("code", "1")));
        Assert.Equal(first.Id, office.Show("customers", "1").Id);
    }

    [Fact]
    public void Backup_is_consistent_restorable_and_never_overwrites_a_file()
    {
        var customer = Customer(); var backup = Path.Combine(root, "backup.db"); db.Backup(backup);
        using var copy = new Database(backup); Assert.Equal(customer.Text("name"), new Backoffice(copy).Show("customers", customer.Text("code")).Text("name"));
        Assert.Throws<UserError>(() => db.Backup(backup)); Assert.Throws<UserError>(() => db.Backup(db.Path));
    }

    [Fact]
    public void Arbitrary_SQLite_database_is_rejected_without_adopting_or_modifying_it()
    {
        var foreign = Path.Combine(root, "foreign.db");
        using (var connection = new SqliteConnection($"Data Source={foreign};Pooling=False")) { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE unrelated(value TEXT); INSERT INTO unrelated VALUES('keep')"; command.ExecuteNonQuery(); }
        var bytes = File.ReadAllBytes(foreign);
        Assert.Throws<UserError>(() => new Database(foreign));
        Assert.Equal(bytes, File.ReadAllBytes(foreign));
    }

    [Theory]
    [InlineData("1.234,50")]
    [InlineData("1e3")]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("NaN")]
    public void Money_never_silently_rounds_or_interprets_ambiguous_input(string input) => Assert.Throws<UserError>(() => Validation.Money(input));
}
