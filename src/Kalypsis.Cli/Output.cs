using System.Globalization;
using System.Text;
using Kalypsis.Cli.Core;

namespace Kalypsis.Cli;

public static class Output
{
    private static readonly CultureInfo Greek = CultureInfo.GetCultureInfo("el-GR");
    public static readonly Dictionary<string, string> Labels = new()
    {
        ["customers"]="Πελάτες", ["insurers"]="Ασφαλιστικές", ["partners"]="Συνεργάτες", ["credits"]="Πιστωτικά",
        ["credited"]="Πιστωτικά (μικτά)", ["credited_net"]="Πιστωτικά (καθαρά)", ["chargeable"]="Μικτά μετά τα πιστωτικά",
        ["chargeable_net"]="Καθαρά μετά τα πιστωτικά", ["customer_credit"]="Πιστωτικό πελάτη", ["void_reason"]="Αιτία ακύρωσης εγγραφής",
        ["id"]="ID",["code"]="Κωδικός",["name"]="Ονομασία",["kind"]="Τύπος",["first_name"]="Όνομα",["last_name"]="Επώνυμο",["vat"]="ΑΦΜ",["email"]="Email",["phone"]="Τηλέφωνο",["city"]="Πόλη",
        ["number"]="Αριθμός",["customer"]="Πελάτης",["insurer"]="Ασφαλιστική",["partner"]="Συνεργάτης",["branch"]="Κλάδος",["plate"]="Πινακίδα",["start"]="Έναρξη",["end"]="Λήξη",["gross"]="Μικτά",["net"]="Καθαρά",["currency"]="Νόμισμα",
        ["commission"]="Προμήθεια %",["office_rate"]="Προμήθεια έδρας %",["partner_rate"]="Προμήθεια συνεργάτη %",["received"]="Εισπραχθέντα",["outstanding"]="Ανεξόφλητο",["office_expected"]="Προμήθεια έδρας",["office_received"]="Εισπραχθείσα προμήθεια έδρας",["partner_expected"]="Προμήθεια συνεργάτη",
        ["policy"]="Συμβόλαιο",["policy_code"]="Κωδικός συμβολαίου",["policy_id"]="ID συμβολαίου",["customer_id"]="ID πελάτη",["insurer_id"]="ID ασφαλιστικής",["partner_id"]="ID συνεργάτη",["previous_id"]="Προηγούμενο συμβόλαιο",["date"]="Ημερομηνία",["amount"]="Ποσό",["method"]="Μέθοδος",["status"]="Κατάσταση",["reason"]="Αιτιολογία",["created_at"]="Δημιουργήθηκε",["action"]="Ενέργεια",["occurred_at"]="Ημερομηνία ενέργειας",["before_json"]="Πριν",["after_json"]="Μετά",["policies"]="Συμβόλαια",["active_policies"]="Ενεργά συμβόλαια",["path"]="Αρχείο"
    };
    public static string Value(object? value)
    {
        if (value is null) return "—";
        if (value is decimal amount) return amount.ToString("0.00", Greek);
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return string.Concat(text.Select(character => char.IsControl(character) ? ' ' : character));
    }
    internal static string Display(string key, object? value)
    {
        if (key is "kind" or "status" or "method" or "action") return Convert.ToString(value, CultureInfo.InvariantCulture) switch
        {
            "person"=>"Φυσικό πρόσωπο", "company"=>"Νομικό πρόσωπο", "active"=>"Ενεργό", "expired"=>"Ληγμένο", "scheduled"=>"Μελλοντικό", "cancelled"=>"Ακυρωμένο", "renewed"=>"Ανανεωμένο", "posted"=>"Καταχωρημένη", "void"=>"Ακυρωμένη εγγραφή", "cash"=>"Μετρητά", "bank"=>"Τράπεζα", "card"=>"Κάρτα", "other"=>"Άλλο", "create"=>"Δημιουργία", "update"=>"Επεξεργασία", "delete"=>"Διαγραφή", "cancel"=>"Ακύρωση συμβολαίου", "renewal"=>"Ανανέωση", _=>Value(value)
        };
        return Value(value);
    }
    public static void Write(TextWriter output, CommandResult result, string entity = "", bool detail = false)
    {
        if (TerminalUi.IsLive(output)) { new TerminalUi(output).Result(result, entity, detail); return; }
        output.WriteLine(result.Message);
        if (result.Records.Count == 0) { output.WriteLine("Δεν υπάρχουν εγγραφές."); return; }
        if (detail)
        {
            foreach (var row in result.Records) foreach (var (key, value) in row) output.WriteLine($"{Labels.GetValueOrDefault(key, key)}: {Display(key, value)}");
            return;
        }
        var columns = entity switch
        {
            "customers" => new[] { "code", "name", "phone", "email" },
            "insurers" or "partners" => ["code", "name", "phone", "email"],
            "policies" => ["code", "number", "customer", "gross", "currency", "status"],
            "receipts" => ["code", "policy", "amount", "currency", "date", "status"],
            "credits" => ["code", "policy", "amount", "net", "currency", "status"],
            "summary" => ["currency", "active_policies", "gross", "received", "outstanding", "office_received"],
            _ => result.Records[0].Keys.ToArray()
        };
        var width = 110;
        if (ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected)
        { try { width = Math.Max(40, Console.WindowWidth - 1); } catch (IOException) { } }
        var maximum = Math.Max(5, (width - (columns.Length - 1) * 3) / columns.Length);
        var sizes = columns.Select(key => Math.Min(maximum, Math.Max(Labels.GetValueOrDefault(key, key).Length, result.Records.Max(row => Display(key, row.GetValueOrDefault(key)).Length)))).ToArray();
        string Fit(string value, int size) => (value.Length <= size ? value : value[..Math.Max(0, size - 1)] + "…").PadRight(size);
        output.WriteLine(string.Join(" | ", columns.Select((key, index) => Fit(Labels.GetValueOrDefault(key, key), sizes[index]))));
        output.WriteLine(string.Join("-+-", sizes.Select(size => new string('-', size))));
        foreach (var row in result.Records) output.WriteLine(string.Join(" | ", columns.Select((key, index) => Fit(Display(key, row.GetValueOrDefault(key)), sizes[index]))));
        output.WriteLine($"{result.Records.Count} εμφανιζόμενες εγγραφές. Πλήρη πεδία: show <κωδικός> ή --json.");
    }

    public static void Csv(string path, IReadOnlyList<Row> rows)
    {
        if (rows.Count == 0) throw new UserError("Δεν υπάρχουν εγγραφές για εξαγωγή.");
        var fullPath = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (File.Exists(fullPath)) throw new UserError("Το αρχείο εξαγωγής υπάρχει ήδη. Επιλέξτε νέο όνομα.");
        using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(true));
        static string Escape(string value)
        {
            if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var keys = rows.SelectMany(row => row.Keys).Distinct().ToArray();
        writer.WriteLine(string.Join(';', keys.Select(key => Escape(Labels.GetValueOrDefault(key, key)))));
        foreach (var row in rows) writer.WriteLine(string.Join(';', keys.Select(key => Escape(Display(key, row.GetValueOrDefault(key))))));
    }
}
