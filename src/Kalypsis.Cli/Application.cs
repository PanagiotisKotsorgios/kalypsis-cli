using System.Text.Json;
using Kalypsis.Cli.Core;
using Microsoft.Data.Sqlite;

namespace Kalypsis.Cli;

public static class Application
{
    public const string Version = "0.1.0";
    public static string DefaultDatabase => Environment.GetEnvironmentVariable("KALYPSIS_CLI_DB")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KalypsisCli", "backoffice.db");
    public static int Run(string[] args, TextReader input, TextWriter output, TextWriter error, bool interactive = false)
    {
        var json = args.Contains("--json");
        try
        {
            var parsed = Arguments.Parse(args); json = parsed.Options.ContainsKey("json");
            if (parsed.Options.ContainsKey("version") || parsed.Words.SequenceEqual(["version"]))
            { if (json) output.WriteLine(JsonSerializer.Serialize(new { version = Version })); else output.WriteLine($"KALYPSIS CLI {Version}"); return 0; }
            if (parsed.Options.ContainsKey("help") || parsed.Words.FirstOrDefault() == "help") { output.WriteLine(Help); return 0; }
            if (parsed.Words.Count == 0)
            {
                parsed.Validate([], 0);
                if (!interactive) { output.WriteLine(Help); return 0; }
                Menu(parsed.Options.GetValueOrDefault("db", DefaultDatabase), input, output, error); return 0;
            }
            var command = parsed.Words[0];
            if (command is "init" or "summary") parsed.Validate([], 1);
            else if (command == "backup") parsed.Validate(["output"], 1);
            else if (command == "export")
            {
                if (parsed.Words.Count < 2) throw new UserError("Σύνταξη: export <οντότητα> --output αρχείο.csv");
                var entity = Backoffice.Entity(parsed.Words[1]); parsed.Validate(Arguments.Filters(entity).Except(["limit"]).Append("output"), 2);
            }
            else ValidateEntity(parsed);
            using var db = new Database(parsed.Options.GetValueOrDefault("db", DefaultDatabase));
            var service = new Backoffice(db); CommandResult result; var category = ""; var detail = false;
            switch (command)
            {
                case "init": result = new("Η τοπική βάση είναι έτοιμη.", [new Row { ["path"] = db.Path }]); detail = true; break;
                case "summary": category = "summary"; result = new("Συνοπτική εικόνα γραφείου ανά νόμισμα", service.Summary()); break;
                case "backup":
                    var backup = Required(parsed, "output"); db.Backup(backup);
                    result = new("Το αντίγραφο ασφαλείας δημιουργήθηκε.", [new Row { ["path"] = Path.GetFullPath(backup) }]); detail = true; break;
                case "export":
                    category = Backoffice.Entity(parsed.Words[1]); var destination = Required(parsed, "output");
                    var exported = service.List(category, parsed.Options, true);
                    if (Path.GetFullPath(destination).Equals(db.Path, StringComparison.OrdinalIgnoreCase)) throw new UserError("Η εξαγωγή δεν μπορεί να αντικαταστήσει τη βάση.");
                    Output.Csv(destination, exported); result = new($"Εξαγωγή {exported.Count} εγγραφών.", [new Row { ["path"] = Path.GetFullPath(destination) }]); detail = true; break;
                default:
                    category = Backoffice.Entity(command); var verb = parsed.Words[1]; var reference = parsed.Words.Count > 2 ? parsed.Words[2] : "";
                    detail = verb is "show" or "add" or "update" or "renew" or "cancel" or "void";
                    result = verb switch
                    {
                        "list" => new("Λίστα εγγραφών", service.List(category, parsed.Options)),
                        "show" => new("Στοιχεία εγγραφής", [service.Show(category, reference)]),
                        "add" => new("Η εγγραφή αποθηκεύτηκε.", [service.Add(category, parsed.Options)]),
                        "update" => new("Οι αλλαγές αποθηκεύτηκαν.", [service.Update(category, reference, parsed.Options)]),
                        "delete" => Delete(service, category, reference, parsed),
                        "void" => new("Η είσπραξη ακυρώθηκε· το ιστορικό διατηρήθηκε.", [service.VoidReceipt(reference, Required(parsed, "reason"))]),
                        "cancel" => new("Το συμβόλαιο ακυρώθηκε. Δεν καταχωρήθηκε επιστροφή χρημάτων.", [service.CancelPolicy(reference, Required(parsed, "reason"))]),
                        "renew" => new("Η ανανέωση αποθηκεύτηκε με σύνδεση στο προηγούμενο συμβόλαιο.", [service.Renew(reference, parsed.Options)]),
                        "history" => new("Ιστορικό εγγραφής", service.History(category, reference)),
                        _ => throw new UserError("Μη υποστηριζόμενη ενέργεια.")
                    };
                    break;
            }
            if (json) output.WriteLine(JsonSerializer.Serialize(new { success = true, message = result.Message, count = result.Records.Count, records = result.Records }));
            else Output.Write(output, result, category, detail);
            return 0;
        }
        catch (UserError exception) { return Failure(exception.Message, 2); }
        catch (SqliteException exception)
        {
            return Failure(exception.SqliteExtendedErrorCode switch
            {
                787 => "Η ενέργεια μπλοκαρίστηκε επειδή υπάρχουν συνδεδεμένα δεδομένα.",
                2067 or 1555 => "Υπάρχει ήδη εγγραφή με αυτόν τον κωδικό ή αριθμό.",
                _ => "Δεν ήταν δυνατή η πρόσβαση στη βάση. Ελέγξτε το αρχείο, τα δικαιώματα και αν χρησιμοποιείται από άλλη διεργασία."
            }, 3);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return Failure("Δεν ήταν δυνατή η πρόσβαση στο αρχείο ή στην τοποθεσία που ορίσατε.", 3); }

        int Failure(string message, int code)
        {
            if (json) error.WriteLine(JsonSerializer.Serialize(new { success = false, error = message, exitCode = code }));
            else error.WriteLine("Σφάλμα: " + message);
            return code;
        }
    }

    private static string Required(Arguments arguments, string key) => arguments.Options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new UserError($"Λείπει --{key}.");
    private static CommandResult Delete(Backoffice service, string entity, string reference, Arguments arguments)
    { RequireYes(arguments); service.Delete(entity, reference); return new("Η εγγραφή διαγράφηκε.", []); }
    private static void RequireYes(Arguments arguments)
    { if (!arguments.Options.ContainsKey("yes")) throw new UserError("Προσθέστε --yes για επιβεβαίωση αυτής της ενέργειας."); }

    private static void ValidateEntity(Arguments parsed)
    {
        var entity = Backoffice.Entity(parsed.Words[0]);
        if (parsed.Words.Count < 2) throw new UserError("Προσθέστε ενέργεια, π.χ. list, add ή show.");
        var verb = parsed.Words[1];
        switch (verb)
        {
            case "list": parsed.Validate(Arguments.Filters(entity), 2); break;
            case "show" or "history": parsed.Validate([], 3); break;
            case "add": parsed.Validate(Arguments.Fields(entity).Append("code"), 2); break;
            case "update":
                if (entity == "receipts") throw new UserError("Χρησιμοποιήστε receipts void και νέα καταχώρηση.");
                parsed.Validate(Arguments.Fields(entity), 3);
                if (!Arguments.Fields(entity).Any(parsed.Options.ContainsKey)) throw new UserError("Δώστε τουλάχιστον ένα πεδίο για αλλαγή.");
                break;
            case "delete": parsed.Validate(["yes"], 3); RequireYes(parsed); break;
            case "void" when entity == "receipts": parsed.Validate(["reason", "yes"], 3); RequireYes(parsed); break;
            case "cancel" when entity == "policies": parsed.Validate(["reason", "yes"], 3); RequireYes(parsed); break;
            case "renew" when entity == "policies": parsed.Validate(["number", "start", "end", "gross", "net", "office-rate", "partner-rate"], 3); break;
            default: throw new UserError("Μη υποστηριζόμενη ενέργεια. Εκτελέστε kalypsis help.");
        }
    }

    public const string Help = """
        KALYPSIS CLI 0.1.0 — Backoffice MVP

        Χρήση: kalypsis <εντολή> [επιλογές]
        Χωρίς ορίσματα: διαδραστικό μενού.

        init                              Δημιουργία τοπικής βάσης
        summary                           Σύνοψη ανά νόμισμα
        customers|insurers|partners        Πελάτες, ασφαλιστικές, συνεργάτες
        policies                          Συμβόλαια
        receipts                          Πραγματικές εισπράξεις

        <οντότητα> list [--search κείμενο] [--limit 50]
        <οντότητα> show <ID ή κωδικός>
        <οντότητα> add <πεδία>
        <οντότητα> update <ID ή κωδικός> <πεδία>
        <οντότητα> delete <ID ή κωδικός> --yes
        <οντότητα> history <ID ή κωδικός>

        customers add --first-name Όνομα --last-name Επώνυμο
          [--phone ... --email ... --vat 9ψηφία --city ...]
        customers add --kind company --name "Επωνυμία"
        insurers add --name "Ασφαλιστική"
        partners add --name "Συνεργάτης" [--commission 10]
        policies add --number P123 --customer C-000001 --insurer I-000001
          --start 2026-01-01 --end 2026-12-31 --gross 100 --net 80
          [--branch Αυτοκίνητο --plate ABC1234 --currency EUR]
          [--partner A-000001 --partner-rate 10 --office-rate 6]
        policies list [--customer ... --insurer ... --partner ...]
          [--status active|expired|scheduled|cancelled|renewed]
          [--from yyyy-MM-dd --to yyyy-MM-dd] (ημερομηνία έναρξης)
        policies renew P-000001 --number P124 --start 2027-01-01
          --end 2027-12-31 [--gross 110 --net 85]
        policies cancel P-000001 --reason "Αιτία" --yes
        receipts add --policy P-000001 --amount 50 [--date yyyy-MM-dd]
          [--number R123 --method cash|bank|card|other]
        receipts list [--policy ... --from ... --to ... --status posted|void]
        receipts void R-000001 --reason "Λανθασμένη εγγραφή" --yes
        export <οντότητα> --output αρχείο.csv [φίλτρα λίστας]
        backup --output αντίγραφο.db

        Καθολικές επιλογές: --db <αρχείο.db> --json --help --version
        Ποσά: 100.50 ή 100,50, χωρίς διαχωριστικά χιλιάδων.
        --office-rate: ποσοστό που κρατά το γραφείο επί των καθαρών.
        Οι εξαγωγές περιλαμβάνουν όλα τα αποτελέσματα των φίλτρων.
        Οι εισπράξεις διορθώνονται με void και νέα καταχώρηση.
        Η ακύρωση συμβολαίου δεν εκτελεί επιστροφή χρημάτων.
        """;

    private static void Menu(string path, TextReader input, TextWriter output, TextWriter error)
    {
        output.WriteLine($"KALYPSIS CLI {Version} — Backoffice\nΤοπική βάση: {Path.GetFullPath(path)}");
        while (true)
        {
            output.WriteLine("\n1 Πελάτες  2 Ασφαλιστικές  3 Συνεργάτες  4 Συμβόλαια  5 Εισπράξεις\n6 Σύνοψη   7 Εξαγωγή CSV  8 Αντίγραφο ασφαλείας  0 Έξοδος");
            output.Write("Επιλογή: "); var choice = input.ReadLine(); if (choice is null or "0") return;
            var args = new List<string>();
            if (choice == "6") args.Add("summary");
            else if (choice is "7" or "8")
            {
                args.Add(choice == "7" ? "export" : "backup");
                if (choice == "7") { output.Write("Οντότητα (customers/insurers/partners/policies/receipts): "); args.Add(input.ReadLine() ?? ""); }
                output.Write("Νέο αρχείο προορισμού: "); args.AddRange(["--output", input.ReadLine() ?? ""]);
            }
            else if (int.TryParse(choice, out var index) && index is >= 1 and <= 5)
            {
                var entity = Backoffice.Entities[index - 1];
                output.WriteLine(entity == "receipts" ? "1 Λίστα  2 Νέα εγγραφή  3 Προβολή  5 Ακύρωση είσπραξης  0 Επιστροφή" : "1 Λίστα  2 Νέα εγγραφή  3 Προβολή  4 Επεξεργασία  5 Διαγραφή  0 Επιστροφή");
                output.Write("Ενέργεια: "); var action = input.ReadLine(); if (action is null) return; if (action == "0") continue;
                var verb = action switch { "1" => "list", "2" => "add", "3" => "show", "4" => "update", "5" => entity == "receipts" ? "void" : "delete", _ => "" };
                if (verb == "") continue; args.AddRange([entity, verb]);
                if (verb is "show" or "update" or "delete" or "void") { output.Write("ID ή κωδικός: "); args.Add(input.ReadLine() ?? ""); }
                if (verb is "delete" or "void")
                {
                    output.Write("Επιβεβαίωση (ναι): "); if (input.ReadLine()?.Trim().ToLowerInvariant() is not ("ναι" or "yes")) continue;
                    args.Add("--yes"); if (verb == "void") { output.Write("Αιτία: "); args.AddRange(["--reason", input.ReadLine() ?? ""]); }
                }
                if (verb is "add" or "update")
                {
                    output.WriteLine("Αφήστε κενά τα προαιρετικά πεδία. Στην επεξεργασία δώστε μόνο τις αλλαγές.");
                    foreach (var field in Arguments.Fields(entity))
                    {
                        output.Write($"{Prompt(field)}: "); var value = input.ReadLine(); if (value is null) return;
                        if (value.Length > 0) args.AddRange(["--" + field, value]);
                    }
                }
            }
            else continue;
            args.AddRange(["--db", path]); Run(args.ToArray(), input, output, error);
        }
    }
    private static string Prompt(string field) => field switch
    {
        "kind"=>"Τύπος (person=φυσικό, company=νομικό, κενό=φυσικό)", "first-name"=>"Όνομα", "last-name"=>"Επώνυμο", "name"=>"Επωνυμία / ονοματεπώνυμο", "customer"=>"Κωδικός πελάτη", "insurer"=>"Κωδικός ασφαλιστικής", "partner"=>"Κωδικός συνεργάτη", "policy"=>"Κωδικός συμβολαίου", "office-rate"=>"Προμήθεια έδρας %", "partner-rate"=>"Προμήθεια συνεργάτη %", "date" or "start" or "end"=>Output.Labels.GetValueOrDefault(field,field)+" (yyyy-MM-dd)", _=>Output.Labels.GetValueOrDefault(field,field)
    };
}
