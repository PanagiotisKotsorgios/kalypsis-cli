using System.Text;
using System.Text.Json;
using Xunit;

namespace Kalypsis.Cli.Tests;

public sealed class CommandTests : IDisposable
{
    private readonly string parent = Path.GetFullPath(Environment.GetEnvironmentVariable("KALYPSIS_CLI_TEST_ROOT") ?? Path.Combine(Path.GetTempPath(), "kalypsis-cli-tests"));
    private readonly string root;
    public CommandTests() { root = Path.Combine(parent, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
    public void Dispose() { if (!Path.GetFullPath(root).StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(); Directory.Delete(root, true); }
    private (int Code, string Out, string Error) Run(params string[] args)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        var code = Application.Run([.. args, "--db", Path.Combine(root, "office.db")], new StringReader(""), output, error);
        return (code, output.ToString(), error.ToString());
    }
    [Fact]
    public void Help_and_version_do_not_create_a_database()
    {
        Assert.Contains("Backoffice MVP", Run("--help").Out);
        Assert.Equal(Application.Version, JsonDocument.Parse(Run("--version", "--json").Out).RootElement.GetProperty("version").GetString());
        Assert.False(File.Exists(Path.Combine(root, "office.db")));
    }
    [Fact]
    public void Json_commands_are_machine_readable_and_keep_errors_out_of_stdout()
    {
        var added = Run("customers", "add", "--first-name", "Μαρία", "--last-name", "Δοκιμή", "--json"); Assert.Equal(0, added.Code);
        var code = JsonDocument.Parse(added.Out).RootElement.GetProperty("records")[0].GetProperty("code").GetString()!;
        Assert.Equal(0, Run("customers", "show", code, "--json").Code);
        var missing = Run("customers", "show", "NOT-FOUND", "--json"); Assert.Equal(2, missing.Code); Assert.Empty(missing.Out);
        Assert.False(JsonDocument.Parse(missing.Error).RootElement.GetProperty("success").GetBoolean());
    }
    [Fact]
    public void Typo_options_and_unconfirmed_delete_do_not_write_records()
    {
        Assert.Equal(2, Run("customers", "add", "--frist-name", "Test").Code);
        Assert.False(File.Exists(Path.Combine(root, "office.db")));
        Run("customers", "add", "--first-name", "Test", "--last-name", "Person");
        Assert.Equal(2, Run("customers", "delete", "C-000001").Code);
        Assert.Equal(0, Run("customers", "show", "C-000001").Code);
        Assert.Equal(0, Run("customers", "delete", "C-000001", "--yes").Code);
    }
    [Fact]
    public void Interactive_menu_creates_a_real_customer_and_exits_cleanly()
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        var input = new StringReader("1\n2\n\nΜαρία\nΔοκιμή\n\n\n\n\nΑθήνα\n0\n");
        Assert.Equal(0, Application.Run(["--db", Path.Combine(root, "office.db")], input, output, error, true));
        Assert.Empty(error.ToString());
        var records = JsonDocument.Parse(Run("customers", "list", "--json").Out).RootElement.GetProperty("records");
        Assert.Equal("Μαρία Δοκιμή", records[0].GetProperty("name").GetString());
        Assert.Equal("Αθήνα", records[0].GetProperty("city").GetString());
    }

    [Fact]
    public void Csv_uses_UTF8_BOM_and_current_filters_without_list_limit_and_protects_formulas()
    {
        Run("customers", "add", "--first-name", "Μαρία", "--last-name", "Δοκιμή", "--city", "Αθήνα");
        Run("customers", "add", "--first-name", "=HYPERLINK(test)", "--last-name", "Δοκιμή", "--city", "Αθήνα");
        Run("customers", "add", "--first-name", "Νίκος", "--last-name", "Άλλος", "--city", "Πάτρα");
        var csv = Path.Combine(root, "customers.csv"); var result = Run("export", "customers", "--search", "αθηνα", "--output", csv);
        Assert.Equal(0, result.Code);
        Assert.Equal(new byte[] { 239, 187, 191 }, File.ReadAllBytes(csv)[..3]);
        var text = File.ReadAllText(csv, Encoding.UTF8); Assert.Contains("Μαρία", text); Assert.DoesNotContain("Νίκος", text); Assert.Contains("'=HYPERLINK", text);
        Assert.Equal(3, File.ReadAllLines(csv).Length);
        Assert.Equal(2, Run("export", "customers", "--output", csv).Code);
    }
    [Fact]
    public void Invalid_arguments_have_a_predictable_exit_code()
    {
        foreach (var args in new string[][] { ["customers", "add", "--name"], ["customers", "list", "--limit", "0"],
            ["customers", "list", "--status", "active"], ["customers", "list", "--search", "x", "--search", "y"] })
            Assert.Equal(2, Run(args).Code);
    }
}
