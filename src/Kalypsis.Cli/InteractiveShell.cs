using Kalypsis.Cli.Core;

namespace Kalypsis.Cli;

public static class InteractiveShell
{
    private static readonly string[] Names = ["Πελάτες", "Ασφαλιστικές", "Συνεργάτες", "Συμβόλαια", "Εισπράξεις", "Πιστωτικά"];
    public static void Run(string path, TextReader input, TextWriter output, TextWriter error)
    {
        var ui = new TerminalUi(output);
        while (true)
        {
            ui.Screen("Πίνακας γραφείου", path);
            using (var db = new Database(path))
            {
                var service = new Backoffice(db); var counts = service.Overview();
                ui.Table(["Πελάτες", "Εταιρείες", "Συνεργάτες", "Συμβόλαια", "Εισπράξεις", "Πιστωτικά"],
                    [Backoffice.Entities.Select(key => new TerminalCell(counts.Text(key), Tone.Cyan, true)).ToArray()]);
            }
            ui.Line();
            ui.Actions([
                ("[1]", "Πελάτες", "Στοιχεία επικοινωνίας και φορολογικά στοιχεία", Tone.Blue),
                ("[2]", "Ασφαλιστικές", "Κατάλογος εταιρειών και επαφών", Tone.Blue),
                ("[3]", "Συνεργάτες", "Στοιχεία συνεργασίας και ποσοστά", Tone.Blue),
                ("[4]", "Συμβόλαια", "Παραγωγή, υπόλοιπα, ανανεώσεις και ιστορικό", Tone.Blue),
                ("[5]", "Εισπράξεις", "Μερικές / πλήρεις εισπράξεις και ακύρωση εγγραφών", Tone.Green),
                ("[6]", "Πιστωτικά", "Ποσά, καθαρά, υπόλοιπα και ιστορικό", Tone.Red),
                ("[7]", "Dashboard KPI", "Γραμμές, μπάρες και μετρήσεις αγοράς / οικονομίας", Tone.Cyan),
                ("[8]", "Οικονομική σύνοψη", "Ξεχωριστά σύνολα ανά νόμισμα", Tone.Cyan),
                ("[9]", "Εξαγωγή CSV", "Φίλτρα και αρχείο συμβατό με Excel", Tone.Green),
                ("[10]", "Αντίγραφο ασφαλείας", "Ασφαλές snapshot της τοπικής βάσης", Tone.Green),
                ("[0]", "Έξοδος", "", Tone.Muted)
            ]);
            ui.Prompt("Επιλογή"); var choice = input.ReadLine()?.Trim();
            if (choice is null or "0") { ui.Line("  Καλή συνέχεια από την KALYPSIS.", Tone.Cyan); return; }
            if (int.TryParse(choice, out var index) && index is >= 1 and <= 6)
            { Entity(Backoffice.Entities[index - 1], Names[index - 1]); continue; }
            if (choice == "7") { Dashboard(); ui.Pause(input); }
            else if (choice == "8")
            { ui.Screen("Οικονομική σύνοψη ανά νόμισμα", path); Execute(["summary"]); ui.Pause(input); }
            else if (choice is "9" or "10")
            {
                ui.Screen(choice == "9" ? "Εξαγωγή CSV" : "Αντίγραφο ασφαλείας", path);
                var args = new List<string> { choice == "9" ? "export" : "backup" };
                if (choice == "9")
                {
                    ui.Line("  1 Πελάτες  2 Ασφαλιστικές  3 Συνεργάτες  4 Συμβόλαια  5 Εισπράξεις  6 Πιστωτικά", Tone.Blue);
                    ui.Prompt("Οντότητα"); var selected = input.ReadLine(); if (selected is null) return;
                    args.Add(int.TryParse(selected, out var selectedIndex) && selectedIndex is >= 1 and <= 6 ? Backoffice.Entities[selectedIndex - 1] : selected);
                    ui.Prompt("Αναζήτηση (προαιρετικά)"); var search = input.ReadLine(); if (search is null) return;
                    if (search.Length > 0) args.AddRange(["--search", search]);
                }
                ui.Prompt("Νέο αρχείο προορισμού"); var destination = input.ReadLine(); if (destination is null) return;
                args.AddRange(["--output", destination]); Execute(args); ui.Pause(input);
            }
            else { ui.Notice("Επιλέξτε έναν αριθμό από το μενού.", false); ui.Pause(input); }
        }

        int Execute(IEnumerable<string> args) => Application.Run([.. args, "--db", path], input, output, error);

        void Dashboard()
        {
            using var db = new Database(path);
            var dashboard = new Backoffice(db).Dashboard();
            ui.Screen("Dashboard KPI", path);
            ui.Table(["Μετρική", "Τιμή"],
            [
                [new TerminalCell("Εκρεμείς συμβόλαια", Tone.Muted), new TerminalCell($"{dashboard.OverduePoliciesCount} / {Output.Value(dashboard.OverduePoliciesTotal)}", Tone.Amber, true)],
                [new TerminalCell("Ανεξόφλητες εισπράξεις", Tone.Muted), new TerminalCell($"{dashboard.UnpaidReceiptsCount} / {Output.Value(dashboard.UnpaidReceiptsTotal)}", Tone.Green, true)],
                [new TerminalCell("Ισοζύγιο πιστωτικών", Tone.Muted), new TerminalCell($"{Output.Value(dashboard.CreditNoteBalance)} ({dashboard.CreditNoteCount})", Tone.Red, true)],
                [new TerminalCell("Πιστωτικό πελάτη", Tone.Muted), new TerminalCell($"{Output.Value(dashboard.CustomerBalances.Sum(x => x.CustomerCredit))}", Tone.Cyan, true)]
            ]);

            var maxOutstanding = Math.Max(1m, dashboard.CustomerBalances.MaxOrDefault(x => x.Outstanding, 0m));
            ui.Line(); ui.Line("  Υπόλοιπα ανά νόμισμα", Tone.Blue);
            if (dashboard.CustomerBalances.Count == 0)
                ui.Line("  Δεν υπάρχουν ανοικτά υπόλοιπα.", Tone.Muted);
            else
                foreach (var item in dashboard.CustomerBalances)
                {
                    var size = Math.Max(1, (int)Math.Round((item.Outstanding / maxOutstanding) * 22m));
                    ui.Line($"  {item.Currency.PadRight(4)} {new string('█', size).PadRight(22)} {Output.Value(item.Outstanding).PadLeft(14)}  |  Πελάτες: {Output.Value(item.CustomerCredit)}", item.Outstanding > 0 ? Tone.Green : Tone.Muted);
                }

            ui.Line(); ui.Line("  Εντοπισμός λήξης 30 / 60 / 90 ημέρες", Tone.Blue);
            var maxExpiry = Math.Max(1m, dashboard.ExpiryBuckets.MaxOrDefault(x => x.Total, 0m));
            foreach (var bucket in dashboard.ExpiryBuckets)
            {
                var size = Math.Max(1, (int)Math.Round((bucket.Total / maxExpiry) * 20m));
                ui.Line($"  {bucket.Bucket.PadRight(6)} {new string('█', size).PadRight(20)} {bucket.Count} συμβόλαια  |  {Output.Value(bucket.Total)}", Tone.Amber);
            }

            ui.Line(); ui.Line("  Ανώτατες προμήθειες συνεργατών", Tone.Blue);
            foreach (var item in dashboard.PartnerCommissions)
            {
                ui.Line($"  {item.Partner.Truncate(24).PadRight(24)} {Output.Value(item.Commission).PadLeft(14)}", Tone.Cyan);
            }
            if (dashboard.PartnerCommissions.Count == 0) ui.Line("  Δεν υπάρχουν εγγραφές συνεργατών.", Tone.Muted);

            ui.Line(); ui.Line("  Εισπράξεις τελευταίων 12 μηνών", Tone.Blue);
            var maxMonth = Math.Max(1m, dashboard.MonthlyCollection.MaxOrDefault(x => x.Amount, 0m));
            foreach (var month in dashboard.MonthlyCollection)
            {
                var size = Math.Max(1, (int)Math.Round((month.Amount / maxMonth) * 20m));
                ui.Line($"  {month.Month.PadRight(10)} {new string('█', size).PadRight(20)} {Output.Value(month.Amount)}", month.Amount > 0 ? Tone.Green : Tone.Muted);
            }
        }

        void Entity(string entity, string title)
        {
            while (true)
            {
                ui.Screen(title, path);
                var actions = new List<(string, string, string, Tone)> {
                    ("[1]", "Λίστα & αναζήτηση", "Πίνακας, σελίδες και άνοιγμα καρτέλας", Tone.Blue),
                    ("[2]", "[+] Νέα εγγραφή", "", Tone.Green),
                    ("[3]", "Προβολή καρτέλας", "", Tone.Cyan)
                };
                if (entity is not ("receipts" or "credits")) actions.Add(("[4]", "[~] Επεξεργασία", "", Tone.Amber));
                actions.Add(("[5]", entity == "receipts" ? "[x] Ακύρωση είσπραξης" : entity == "credits" ? "[x] Ακύρωση πιστωτικού" : "[x] Διαγραφή", "Προσθήκη επιβεβαίωσης πριν τη διαγραφή", Tone.Red));
                actions.Add(("[6]", "Ιστορικό αλλαγών", "", Tone.Muted));
                if (entity == "policies")
                { actions.Add(("[7]", "Ανανέωση συμβολαίου", "", Tone.Green)); actions.Add(("[8]", "Ακύρωση συμβολαίου", "", Tone.Red)); }
                actions.Add(("[0]", "Πίσω στον πίνακα γραφείου", "", Tone.Muted));
                ui.Actions(actions); ui.Prompt("Ενέργεια"); var action = input.ReadLine()?.Trim(); if (action is null or "0") return;
                if (action == "1") { List(entity, title); continue; }
                if (action == "3") { ui.Prompt("ID ή κωδικός"); var code = input.ReadLine(); if (code is null) return; Show(entity, title, code); continue; }
                var verb = action switch { "2" => "add", "4" when entity is not ("receipts" or "credits") => "update", "5" => entity is "receipts" or "credits" ? "void" : "delete", "6" => "history", "7" when entity == "policies" => "renew", "8" when entity == "policies" => "cancel", _ => "" };
                if (verb.Length == 0) { ui.Notice("Μη έγκυρη επιλογή.", false); ui.Pause(input); continue; }
                ui.Screen(title + " / " + ActionTitle(verb), path);
                var args = new List<string> { entity, verb };
                if (verb != "add")
                {
                    ui.Prompt("ID ή κωδικός"); var reference = input.ReadLine(); if (reference is null) return;
                    args.Add(reference);
                    if (verb is "delete" or "void" or "cancel")
                    {
                        if (Execute([entity, "show", reference]) != 0) { ui.Pause(input); continue; }
                        ui.Line(); ui.Line("  [!] Η ενέργεια επηρεάζει την παραπάνω εγγραφή.", Tone.Red);
                        ui.Prompt("Πληκτρολογήστε ναι για επιβεβαίωση");
                        if (input.ReadLine()?.Trim().ToLowerInvariant() is not ("ναι" or "yes")) { ui.Line("  Η ενέργεια ακυρώθηκε.", Tone.Amber); ui.Pause(input); continue; }
                        args.Add("--yes");
                        if (verb != "delete") { ui.Prompt("Αιτία"); var reason = input.ReadLine(); if (reason is null) return; args.AddRange(["--reason", reason]); }
                    }
                }
                if (verb is "add" or "update" or "renew")
                {
                    ui.Line(verb == "update" ? "  Κενό = διατήρηση. /clear = εκκαθάριση προαιρετικού πεδίου." : "  Αφήστε κενά τα προαιρετικά πεδία που δεν θέλετε να ξεκινήσουν.", Tone.Muted);
                    var fields = verb == "renew" ? new[] { "number", "start", "end", "gross", "net", "office-rate", "partner-rate" } : Arguments.Fields(entity);
                    foreach (var field in fields)
                    {
                        ui.Prompt(field == "reason" && entity == "credits" ? "Αιτιολογία πιστωτικού" : FieldPrompt(field)); var value = input.ReadLine(); if (value is null) return;
                        if (value.Length == 0) continue;
                        if (field == "kind") value = value.Trim().ToLowerInvariant() switch { "1" or "φυσικό" => "person", "2" or "νομικό" => "company", _ => value };
                        if (field == "method") value = value.Trim().ToLowerInvariant() switch { "1" or "μετρητά" => "cash", "2" or "τράπεζα" => "bank", "3" or "κάρτα" => "card", "4" or "άλλο" => "other", _ => value };
                        args.AddRange(["--" + field, verb == "update" && value == "/clear" ? "" : value]);
                    }
                }
                ui.Screen(title + " / " + ActionTitle(verb), path); Execute(args); ui.Pause(input);
            }
        }
        void List(string entity, string title)
        {
            var page = 0; var search = "";
            while (true)
            {
                ui.Screen(title + " / Λίστα", path);
                List<Row> rows;
                using (var db = new Database(path))
                {
                    var options = new Dictionary<string, string> { ["limit"] = "1000" };
                    if (search.Length > 0) options["search"] = search;
                    rows = new Backoffice(db).List(entity, options);
                }
                var pageSize = ui.Width < 70 ? 2 : Math.Clamp(ui.Height - 18, 4, 20);
                var pages = Math.Max(1, (int)Math.Ceiling((double)rows.Count / pageSize)); page = Math.Clamp(page, 0, pages - 1);
                ui.Result(new(search.Length > 0 ? "Αποτελέσματα αναζήτησης: " + search : title, rows.Skip(page * pageSize).Take(pageSize).ToArray()), entity, false);
                ui.Line($"  Σελίδα {page + 1}/{pages}  |  {rows.Count} αποτελέσματα", Tone.Cyan);
                if (rows.Count == 1000) ui.Line("  Όριο 1.000 εγγραφών. Αναζητήστε για περιορισμό· το CSV περιλαμβάνει όλα τα αποτελέσματα.", Tone.Amber);
                ui.Line("  [N] Επόμενη  [P] Προηγούμενη  [F] Αναζήτηση  [C] Καθαρισμός", Tone.Blue);
                ui.Line("  [0] Πίσω  |  Γράψτε κωδικό για άνοιγμα καρτέλας", Tone.Muted);
                ui.Prompt("Επιλογή"); var choice = input.ReadLine()?.Trim(); if (choice is null or "0") return;
                switch (choice.ToUpperInvariant())
                {
                    case "N" or "Ν": page = Math.Min(pages - 1, page + 1); break;
                    case "P" or "Π": page = Math.Max(0, page - 1); break;
                    case "C": search = ""; page = 0; break;
                    case "F" or "Φ": ui.Prompt("Αναζήτηση"); search = input.ReadLine()?.Trim() ?? ""; page = 0; break;
                    default: Show(entity, title, choice); break;
                }
            }
        }
        void Show(string entity, string title, string reference)
        {
            Row row;
            try { using var db = new Database(path); row = new Backoffice(db).Show(entity, reference); }
            catch (UserError exception) { ui.Notice(exception.Message, false); ui.Pause(input); return; }
            var fields = row.ToArray(); var pageSize = Math.Max(5, ui.Height - 17); var page = 0;
            var count = (int)Math.Ceiling((double)fields.Length / pageSize);
            while (true)
            {
                ui.Screen(title + " / Καρτέλα " + row.Text("code"), path);
                var shown = new Row(); foreach (var pair in fields.Skip(page * pageSize).Take(pageSize)) shown[pair.Key] = pair.Value;
                ui.Result(new("Στοιχεία εγγραφής", [shown]), entity, true);
                ui.Line($"  Πεδία: σελίδα {page + 1}/{count}", Tone.Muted);
                ui.Line("  [N] Επόμενα πεδία  [P] Προηγούμενα  [0] Πίσω", Tone.Blue);
                ui.Prompt("Επιλογή"); var choice = input.ReadLine()?.Trim().ToUpperInvariant();
                if (choice is null or "0" or "") return;
                if (choice is "N" or "Ν") page = Math.Min(count - 1, page + 1);
                else if (choice is "P" or "Π") page = Math.Max(0, page - 1);
            }
        }
    }
    private static string ActionTitle(string verb) => verb switch
    { "add" => "[+] Νέα εγγραφή", "update" => "Επεξεργασία", "delete" => "Διαγραφή", "show" => "Καρτέλα", "history" => "Ιστορικό", "void" => "Ακύρωση", "cancel" => "Ακύρωση συμβολαίου", "renew" => "Ανανέωση συμβολαίου", _ => verb };
    private static string FieldPrompt(string field) => field switch
    {
        "kind" => "Τύπος: 1 Φυσικό / 2 Νομικό (κενό = φυσικό)", "first-name" => "Όνομα", "last-name" => "Επώνυμο",
        "name" => "Επωνυμία / ονοματεπώνυμο", "customer" => "Κωδικός πελάτη", "insurer" => "Κωδικός ασφαλιστικής",
        "partner" => "Κωδικός συνεργάτη", "policy" => "Κωδικός συμβολαίου", "office-rate" => "Προμήθεια έδρας %", "partner-rate" => "Προμήθεια συνεργάτη %",
        "amount" => "Ποσό", "date" or "start" or "end" => Output.Labels.GetValueOrDefault(field, field) + " (yyyy-MM-dd)",
        "method" => "Μέθοδος: 1 Μετρητά / 2 Τράπεζα / 3 Κάρτα / 4 Άλλο", _ => Output.Labels.GetValueOrDefault(field, field)
    };
}

internal static class DashboardExtensions
{
    public static decimal MaxOrDefault<T>(this IEnumerable<T> source, Func<T, decimal> selector, decimal defaultValue)
    {
        if (source is null) return defaultValue;
        var values = source.Select(selector).ToList();
        return values.Count == 0 ? defaultValue : values.Max();
    }

    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "…";
    }
}
