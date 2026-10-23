using System.Globalization;
using System.Runtime.InteropServices;
using Kalypsis.Cli.Core;

namespace Kalypsis.Cli;

public enum Tone { Normal, Muted, Blue, Cyan, Green, Amber, Red, Header, Alternate, Credit }
public sealed record TerminalCell(string Text, Tone Tone = Tone.Normal, bool Right = false);

/// <summary>Terminal-only presentation. Commands, CSV and JSON retain their plain contracts.</summary>
public sealed class TerminalUi
{
    private readonly TextWriter output;
    private readonly bool color;
    public bool Live { get; }
    private readonly int? fixedWidth;
    public int Width => fixedWidth ?? ConsoleWidth();
    public int Height { get { try { return Live ? Console.WindowHeight : 40; } catch (IOException) { return 40; } } }
    public TerminalUi(TextWriter output, int? width = null, bool? useColor = null)
    {
        this.output = output; fixedWidth = width; Live = IsLive(output);
        color = useColor ?? (Live && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))
            && Environment.GetEnvironmentVariable("TERM") != "dumb" && EnableAnsi());
    }
    public static bool IsLive(TextWriter writer) =>
        ReferenceEquals(writer, Console.Out) && !Console.IsOutputRedirected
        || ReferenceEquals(writer, Console.Error) && !Console.IsErrorRedirected;
    private static int ConsoleWidth()
    { try { return Math.Clamp(Console.WindowWidth - 2, 48, 160); } catch (IOException) { return 100; } }
    private static bool EnableAnsi()
    {
        if (!OperatingSystem.IsWindows()) return true;
        var handle = GetStdHandle(-11);
        return GetConsoleMode(handle, out var mode) && SetConsoleMode(handle, mode | 0x0004);
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr handle, uint mode);
    private static string Escape(Tone tone) => tone switch
    {
        Tone.Muted => "\u001b[38;2;139;159;184m", Tone.Blue => "\u001b[38;2;78;164;255m",
        Tone.Cyan => "\u001b[38;2;65;210;230m", Tone.Green => "\u001b[38;2;74;222;128m",
        Tone.Amber => "\u001b[38;2;251;191;36m", Tone.Red => "\u001b[38;2;255;112;120m",
        Tone.Credit => "\u001b[48;2;166;32;54m\u001b[38;2;255;255;255m",
        Tone.Header => "\u001b[48;2;11;48;92m\u001b[38;2;255;255;255m",
        Tone.Alternate => "\u001b[48;2;20;35;54m\u001b[38;2;225;236;247m",
        _ => "\u001b[38;2;225;236;247m"
    };
    private const string Surface = "\u001b[48;2;12;23;38m";
    private string Paint(string text, Tone tone) => color ? Surface + Escape(tone) + text + "\u001b[0m" : text;
    public void Line(string text = "", Tone tone = Tone.Normal)
    { foreach (var line in Wrap(text, Width)) output.WriteLine(Paint(Fit(line, Width), tone)); }
    public void Prompt(string text)
    {
        var lines = Wrap("  " + Output.Value(text) + " > ", Width).ToArray();
        foreach (var line in lines[..^1]) Line(line, Tone.Cyan);
        output.Write(color ? Surface + Escape(Tone.Cyan) + lines[^1] : lines[^1]);
    }
    public void Screen(string title, string path)
    {
        if (Live)
        {
            if (color) output.Write(Surface + "\u001b[2J\u001b[H");
            else { try { Console.Clear(); } catch (IOException) { } }
            try { Console.Title = $"KALYPSIS CLI {Application.Version} | {title}"; } catch (IOException) { }
        }
        output.WriteLine(Paint(Fit($"  KALYPSIS CLI  {Application.Version}   /   BACKOFFICE", Width), Tone.Header));
        output.WriteLine(Paint(Fit("  " + title, Width), Tone.Header));
        Line("  Τοπική SQLite  |  Χωρίς σύνδεση στο Cloud", Tone.Muted);
        if (Environment.GetEnvironmentVariable("KALYPSIS_CLI_DEMO") == "1") Line("  DEMO / Δοκιμαστικά δεδομένα μόνο για παρουσίαση", Tone.Amber);
        foreach (var line in Wrap("  " + Path.GetFullPath(path), Width)) Line(line, Tone.Muted);
        Line();
    }
    public void Rule() => Line(new string('─', Width), Tone.Blue);
    public void Pause(TextReader input)
    { if (!Live) return; Line(); Prompt("Enter για επιστροφή"); input.ReadLine(); }
    public void Notice(string text, bool success = true) => Line("  " + (success ? "[+] " : "[!] ") + text, success ? Tone.Green : Tone.Red);
    public void Actions(IEnumerable<(string Key, string Title, string Description, Tone Tone)> actions)
    {
        foreach (var (key, title, description, tone) in actions)
        {
            Line("  " + key.PadRight(5) + title, tone);
            if (Width >= 80 && Height >= 37 && description.Length > 0) Line("        " + description, Tone.Muted);
        }
        Line();
    }
    public void Table(string[] headers, IReadOnlyList<TerminalCell[]> rows, bool wrapValues = false)
    {
        if (headers.Length == 0) return;
        var sizes = headers.Select((h, i) => Math.Clamp(Math.Max(Length(h), rows.Count == 0 ? 0 : rows.Max(r => Length(r[i].Text))), 4, headers.Length == 2 ? Width - 18 : 32)).ToArray();
        var available = Width - headers.Length * 3 - 1;
        while (sizes.Sum() > available)
        {
            var largest = Array.IndexOf(sizes, sizes.Max());
            if (sizes[largest] <= 4) break;
            sizes[largest]--;
        }
        void Border(char left, char join, char right) => Line(left + string.Join(join, sizes.Select(size => new string('─', size + 2))) + right, Tone.Blue);
        void Row(TerminalCell[] cells, bool header, int rowIndex)
        {
            var parts = cells.Select((cell, i) => header || wrapValues ? Wrap(cell.Text, sizes[i]).ToArray() : [Fit(cell.Text, sizes[i]).TrimEnd()]).ToArray();
            for (var line = 0; line < parts.Max(p => p.Length); line++)
            {
                output.Write(Paint("│", Tone.Blue));
                for (var i = 0; i < sizes.Length; i++)
                {
                    var value = line < parts[i].Length ? parts[i][line] : "";
                    var tone = header ? Tone.Header : cells[i].Tone == Tone.Normal && rowIndex % 2 == 1 ? Tone.Alternate : cells[i].Tone;
                    output.Write(Paint(" " + Fit(value, sizes[i], cells[i].Right) + " ", tone));
                    output.Write(Paint("│", Tone.Blue));
                }
                output.WriteLine();
            }
        }
        Border('┌', '┬', '┐'); Row(headers.Select(h => new TerminalCell(h)).ToArray(), true, 0); Border('├', '┼', '┤');
        for (var i = 0; i < rows.Count; i++) Row(rows[i], false, i);
        Border('└', '┴', '┘');
    }
    public void Result(CommandResult result, string entity, bool detail)
    {
        Line("  [+] " + result.Message, Tone.Green);
        if (result.Records.Count == 0) { Line("  Δεν υπάρχουν εγγραφές.", Tone.Muted); return; }
        var columns = Columns(entity, result.Records[0], Width);
        if (detail || Width < 70 || entity == "summary" && Width < 105)
        {
            foreach (var row in result.Records)
            {
                if (result.Records.Count > 1) Line("  " + row.Text("code") + " " + row.Text("currency"), Tone.Cyan);
                var fields = detail || entity == "summary" ? row.Keys.ToArray() : columns;
                Table(["Πεδίο", "Τιμή"], fields.Select(key => new[] {
                    new TerminalCell(Output.Labels.GetValueOrDefault(key, key), Tone.Muted), RecordCell(entity, key, row.GetValueOrDefault(key)) }).ToArray(), true);
            }
        }
        else Table(columns.Select(key => ShortLabel(key)).ToArray(), result.Records.Select(row => columns.Select(key => RecordCell(entity, key, row.GetValueOrDefault(key))).ToArray()).ToArray());
        Line($"  {result.Records.Count} εγγραφές στην προβολή  |  Καρτέλα για πλήρη στοιχεία", Tone.Muted);
    }
    private static string ShortLabel(string key) => key switch
    { "active_policies" => "Ενεργά", "office_received" => "Προμ. έδρας", "currency" => "Νόμ.", "credited"=>"Πιστωτικά", "customer_credit"=>"Πιστ. πελάτη", _ => Output.Labels.GetValueOrDefault(key, key) };
    private static string[] Columns(string entity, Row first, int width) => entity switch
    {
        "customers" when width < 105 => ["code", "name", "phone"],
        "customers" => ["code", "name", "phone", "email", "city"],
        "insurers" or "partners" => ["code", "name", "phone", "email"],
        "policies" => ["code", "number", "customer", "gross", "currency", "outstanding", "status"],
        "receipts" => ["code", "policy", "amount", "currency", "date", "status"],
        "credits" => ["code", "policy", "amount", "net", "currency", "date", "status"],
        "summary" => ["currency", "gross", "credited", "received", "outstanding", "customer_credit", "office_received"],
        _ => first.Keys.ToArray()
    };
    private static TerminalCell RecordCell(string entity, string key, object? value)
    {
        var cell = Cell(key, value);
        return entity == "credits" && key is "amount" or "net" or "status" ? cell with { Tone = Tone.Credit } : cell;
    }
    public static TerminalCell Cell(string key, object? value)
    {
        var tone = Tone.Normal;
        if (value is decimal or long or int or double)
        {
            var amount = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            if (amount < 0 || key.Contains("credit", StringComparison.OrdinalIgnoreCase) && amount != 0 || key == "outstanding" && amount > 0) tone = Tone.Credit;
            else if (amount > 0 && key is "received" or "office_received" or "amount") tone = Tone.Green;
            else if (key is "gross" or "net") tone = Tone.Cyan;
        }
        if (key == "status") tone = value?.ToString() switch
        { "cancelled" or "void" or "credit" or "credited" => Tone.Credit, "active" or "posted" => Tone.Green, "expired" => Tone.Amber, "renewed" or "scheduled" => Tone.Cyan, _ => Tone.Muted };
        return new(Output.Display(key, value), tone, value is decimal or long or int or double);
    }
    public static int Length(string value) => new StringInfo(Output.Value(value)).LengthInTextElements;
    public static string Fit(string value, int width, bool right = false)
    {
        var text = new StringInfo(Output.Value(value));
        var clipped = text.LengthInTextElements <= width ? text.String : width > 1 ? text.SubstringByTextElements(0, width - 1) + "…" : "…";
        var padding = new string(' ', Math.Max(0, width - Length(clipped)));
        return right ? padding + clipped : clipped + padding;
    }
    private static IEnumerable<string> Wrap(string value, int width)
    {
        var text = new StringInfo(Output.Value(value));
        if (text.LengthInTextElements == 0) { yield return ""; yield break; }
        for (var i = 0; i < text.LengthInTextElements; i += width)
            yield return text.SubstringByTextElements(i, Math.Min(width, text.LengthInTextElements - i));
    }
}
