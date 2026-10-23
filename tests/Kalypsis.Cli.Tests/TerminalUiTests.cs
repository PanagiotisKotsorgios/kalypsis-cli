using System.Text.RegularExpressions;
using Kalypsis.Cli.Core;
using Xunit;

namespace Kalypsis.Cli.Tests;

public sealed partial class TerminalUiTests
{
    private static readonly Row Policy = new() { ["code"] = "P-000001", ["number"] = "DEMO-100",
        ["customer"] = "Μαρία Παπαδοπούλου", ["gross"] = 120.00m, ["currency"] = "EUR",
        ["outstanding"] = 30.50m, ["status"] = "cancelled" };

    [Theory]
    [InlineData(48)] [InlineData(80)] [InlineData(100)] [InlineData(140)]
    public void Rich_and_plain_tables_fit_the_terminal_and_keep_Greek_values(int width)
    {
        foreach (var color in new[] { true, false })
        {
            using var output = new StringWriter();
            new TerminalUi(output, width, color).Result(new("Συμβόλαια", [Policy]), "policies", false);
            var text = Ansi().Replace(output.ToString(), "");
            Assert.Contains("P-000001", text); Assert.Contains("120,00", text); Assert.Contains("30,50", text);
            Assert.Contains("Ακυρωμένο", text); Assert.Contains("┌", text);
            Assert.All(text.Split(Environment.NewLine), line => Assert.True(TerminalUi.Length(line) <= width, line));
            if (!color) Assert.DoesNotContain("\u001b", output.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("credit_amount", -12.5)] [InlineData("credits", 25)] [InlineData("outstanding", 60)]
    public void Credit_indicators_and_unpaid_balances_have_a_red_background_and_white_text(string key, double amount)
    {
        Assert.Equal(Tone.Credit, TerminalUi.Cell(key, (decimal)amount).Tone);
        using var output = new StringWriter();
        new TerminalUi(output, 100, true).Result(new("Δοκιμή", [new Row { [key] = (decimal)amount }]), "", true);
        Assert.Contains("\u001b[48;2;166;32;54m\u001b[38;2;255;255;255m", output.ToString());
        Assert.Equal(Tone.Green, TerminalUi.Cell("received", 10m).Tone);
        Assert.Equal(Tone.Normal, TerminalUi.Cell("outstanding", 0m).Tone);
    }

    [Fact]
    public void Numeric_cells_are_right_aligned_in_list_tables()
    {
        using var output = new StringWriter();
        new TerminalUi(output, 80, false).Table(["Amount"], [[new TerminalCell("12", Right: true)]]);
        Assert.Contains("│     12 │", output.ToString());
    }

    [Fact]
    public void Details_wrap_without_losing_fields_and_control_sequences_cannot_clear_the_screen()
    {
        var email = "an.extremely.long.email.address.for.testing@example.invalid";
        var row = new Row { ["name"] = "Μαρία", ["email"] = email, ["phone"] = "\u001b[2J2101234567" };
        using var output = new StringWriter();
        new TerminalUi(output, 48, false).Result(new("Στοιχεία", [row]), "customers", true);
        var text = output.ToString();
        Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal); Assert.Contains("Email", text); Assert.Contains("Μαρία", text);
        var letters = string.Concat(text.Where(c => char.IsLetterOrDigit(c) || c is '@' or '.'));
        Assert.Contains(email, letters);
        Assert.Equal("α\u0301β", TerminalUi.Fit("α\u0301β", 2));
        Assert.Equal(2, TerminalUi.Length("α\u0301β"));
    }

    [GeneratedRegex("\u001b\\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex Ansi();
}
