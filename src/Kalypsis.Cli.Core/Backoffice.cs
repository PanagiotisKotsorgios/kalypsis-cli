using System.Globalization;

namespace Kalypsis.Cli.Core;

public sealed record CommandResult(string Message, IReadOnlyList<Row> Records);
public sealed record DashboardResult(
    int OverduePoliciesCount,
    decimal OverduePoliciesTotal,
    int UnpaidReceiptsCount,
    decimal UnpaidReceiptsTotal,
    decimal CreditNoteBalance,
    int CreditNoteCount,
    IReadOnlyList<(string Currency, decimal Outstanding, decimal CustomerCredit)> CustomerBalances,
    IReadOnlyList<(string Bucket, int Count, decimal Total)> ExpiryBuckets,
    IReadOnlyList<(string Partner, decimal Commission)> PartnerCommissions,
    IReadOnlyList<(string Month, decimal Amount)> MonthlyCollection);

public sealed class Backoffice(Database db, DateOnly? currentDate = null)
{
    private readonly DateOnly today = currentDate ?? DateOnly.FromDateTime(DateTime.Today);
    public static readonly string[] Entities = ["customers", "insurers", "partners", "policies", "receipts", "credits"];
    public Row Overview() => db.Query("SELECT (SELECT COUNT(*) FROM customers) AS customers, (SELECT COUNT(*) FROM insurers) AS insurers, (SELECT COUNT(*) FROM partners) AS partners, (SELECT COUNT(*) FROM policies) AS policies, (SELECT COUNT(*) FROM receipts) AS receipts, (SELECT COUNT(*) FROM credits) AS credits").FirstOrDefault() ?? new Row();
    private static string Need(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new UserError($"Λείπει η επιλογή --{key}.");

    public static string Entity(string value) => value switch
    {
        "customer" or "clients" => "customers", "insurer" or "companies" => "insurers", "partner" or "producers" => "partners",
        "policy" => "policies", "receipt" => "receipts", "credit" or "credit-notes" or "creditnotes" => "credits", _ when Entities.Contains(value) => value,
        _ => throw new UserError("Επιλέξτε customers, insurers, partners, policies, receipts ή credits.")
    };
    private static string View(string entity) => entity switch { "policies" => "policy_records", "receipts" => "receipt_records", "credits" => "credit_records", _ => entity };

    public DashboardResult Dashboard()
    {
        var policies = db.Query("SELECT * FROM policy_records").Select(EnrichPolicy).ToList();
        var receipts = db.Query("SELECT * FROM receipt_records");
        var credits = db.Query("SELECT * FROM credit_records");

        var overduePolicies = policies.Where(p => p.Text("status") == "active" && DateOnly.TryParse(p.Text("end"), out var end) && end < today).ToList();
        var unpaidReceipts = receipts.Where(r => r.Text("status") != "void").ToList();
        var postedCredits = credits.Where(c => c.Text("status") != "void").ToList();

        var customerBalances = policies.Where(p => p.Text("status") != "cancelled")
            .GroupBy(p => p.Text("currency"))
            .OrderBy(g => g.Key)
            .Select(g => (Currency: g.Key, Outstanding: g.Sum(p => p.Number("outstanding")), CustomerCredit: g.Sum(p => p.Number("customer_credit"))))
            .ToList();

        var expiryBuckets = new[] { (30, "30η"), (60, "60η"), (90, "90η") }
            .Select(bucket =>
            {
                var matches = policies.Where(p => p.Text("status") == "active"
                    && DateOnly.TryParse(p.Text("end"), out var expiry)
                    && expiry >= today
                    && expiry <= today.AddDays(bucket.Item1)).ToList();
                return (Bucket: bucket.Item2, Count: matches.Count, Total: matches.Sum(p => p.Number("outstanding")));
            })
            .ToList();

        var partnerCommissions = policies.Where(p => !string.IsNullOrWhiteSpace(p.Text("partner")))
            .GroupBy(p => p.Text("partner"))
            .OrderByDescending(g => g.Sum(p => p.Number("partner_expected")))
            .Select(g => (Partner: g.Key, Commission: g.Sum(p => p.Number("partner_expected"))))
            .Take(8)
            .ToList();

        var monthlyCollection = Enumerable.Range(0, 12).Select(offset =>
        {
            var month = today.AddMonths(offset - 11);
            var total = receipts.Where(r => r.Text("status") != "void"
                && DateOnly.TryParse(r.Text("date"), out var date)
                && date.Year == month.Year && date.Month == month.Month)
                .Sum(r => r.Number("amount"));
            return (Month: month.ToString("MMM yyyy", CultureInfo.GetCultureInfo("el-GR")), Amount: total);
        }).ToList();

        return new DashboardResult(
            overduePolicies.Count,
            overduePolicies.Sum(p => p.Number("outstanding")),
            unpaidReceipts.Count,
            unpaidReceipts.Sum(r => r.Number("amount")),
            postedCredits.Sum(c => c.Number("amount")),
            postedCredits.Count,
            customerBalances,
            expiryBuckets,
            partnerCommissions,
            monthlyCollection);
    }

    public Row Show(string entity, string reference)
    {
        entity = Entity(entity);
        var numericId = long.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : -1;
        var row = db.Query($"SELECT * FROM {View(entity)} WHERE id=$id OR code=$code COLLATE NOCASE ORDER BY CASE WHEN id=$id THEN 0 ELSE 1 END LIMIT 1", ("$id", numericId), ("$code", reference)).FirstOrDefault()
            ?? throw new UserError($"Δεν βρέθηκε εγγραφή {entity} με ID/κωδικό «{reference}».");
        return entity == "policies" ? EnrichPolicy(row) : row;
    }

    public List<Row> List(string entity, IReadOnlyDictionary<string, string> options, bool export = false)
    {
        entity = Entity(entity);
        var clauses = new List<string>(); var parameters = new List<(string, object?)>();
        if (options.TryGetValue("search", out var search) && search.Length > 0)
        {
            var columns = entity switch
            {
                "customers" => new[] { "code", "name", "vat", "phone", "email", "city" },
                "policies" => ["code", "number", "customer", "insurer", "partner", "branch", "plate"],
                "receipts" => ["code", "number", "policy", "policy_code", "customer"],
                "credits" => ["code", "number", "policy", "policy_code", "customer", "reason"],
                _ => ["code", "name", "phone", "email"]
            };
            clauses.Add("(" + string.Join(" OR ", columns.Select(column => $"contains_text({column},$search)")) + ")");
            parameters.Add(("$search", search));
        }
        foreach (var (option, column) in new[] { ("customer", "customer_id"), ("insurer", "insurer_id"), ("partner", "partner_id"), ("policy", "policy_id") })
        {
            if (!options.TryGetValue(option, out var reference)) continue;
            var id = Show(option switch { "customer" => "customers", "insurer" => "insurers", "partner" => "partners", _ => "policies" }, reference).Id;
            clauses.Add($"{column}=${option}"); parameters.Add(("$" + option, id));
        }
        foreach (var key in new[] { "from", "to" })
        {
            if (!options.TryGetValue(key, out var date)) continue;
            clauses.Add($"{(entity == "policies" ? "start" : "date")} {(key == "from" ? ">=" : "<=")} ${key}");
            parameters.Add(("$" + key, Validation.Date(date)));
        }
        if (options.TryGetValue("from", out var from) && options.TryGetValue("to", out var to) && string.CompareOrdinal(from, to) > 0) throw new UserError("Η ημερομηνία Από πρέπ��ι να είναι πριν ή ίδια με την έως.");
        if (options.TryGetValue("status", out var status))
        {
            if (entity == "policies" && status is "active" or "expired" or "scheduled")
            {
                clauses.Add(status switch { "expired" => "status='active' AND end<$today", "scheduled" => "status='active' AND start>$today", _ => "status='active' AND start<=$today AND end>=$today" });
                parameters.Add(("$today", today.ToString("yyyy-MM-dd")));
            }
            else
            {
                if (!(entity == "policies" ? new[] { "cancelled", "renewed" } : new[] { "posted", "void" }).Contains(status)) throw new UserError("Μη έγκυρη κατάσταση. Δείτε τη βοήθεια.");
                clauses.Add("status=$status"); parameters.Add(("$status", status));
            }
        }
        var limit = 50;
        if (options.TryGetValue("limit", out var rawLimit) && (!int.TryParse(rawLimit, out limit) || limit is < 1 or > 1000)) throw new UserError("Το --limit πρέπει να είναι από 1 έως 1000.");
        var where = clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "";
        var rows = db.Query($"SELECT * FROM {View(entity)}{where} ORDER BY id DESC{(export ? "" : " LIMIT " + limit)}", parameters.ToArray());
        return entity == "policies" ? rows.Select(EnrichPolicy).ToList() : rows;
    }

    public Row Add(string entity, IReadOnlyDictionary<string, string> options) => db.Atomic(() =>
    {
        entity = Entity(entity);
        Dictionary<string, object?> fields; string prefix;
        switch (entity)
        {
            case "customers": fields = CustomerFields(options, null); prefix = "C"; break;
            case "insurers": fields = PartyFields(options, false); prefix = "I"; break;
            case "partners": fields = PartyFields(options, true); prefix = "A"; break;
            case "policies": fields = PolicyFields(options, null); prefix = "P"; break;
            case "receipts": fields = ReceiptFields(options); prefix = "R"; break;
            case "credits": fields = CreditFields(options); prefix = "CR"; break;
            default: throw new UserError("Μη υποστηριζόμενη εγγραφή.");
        }
        var id = db.Insert(entity, fields, prefix); var row = Show(entity, id.ToString(CultureInfo.InvariantCulture));
        db.Audit(entity, id, "create", null, row); return row;
    });

    public Row Update(string entity, string reference, IReadOnlyDictionary<string, string> options) => db.Atomic(() =>
    {
        entity = Entity(entity);
        if (entity is "receipts" or "credits") throw new UserError("Οι οικονομικές εγγραφές δεν τροποποιούνται. Χρησιμοποιήστε void και νέα καταχώρηση.");
        var before = Show(entity, reference);
        var fields = entity switch { "customers" => CustomerFields(options, before), "policies" => PolicyFields(options, before), _ => PartyFields(options, entity == "partners", before) };
        db.Update(entity, before.Id, fields); var after = Show(entity, reference);
        db.Audit(entity, before.Id, "update", before, after); return after;
    });

    public void Delete(string entity, string reference) => db.Atomic(() =>
    {
        entity = Entity(entity);
        if (entity is "receipts" or "credits") throw new UserError("Χρησιμοποιήστε void για ακύρωση λανθασμένης οικον��μικής εγγραφής· το delete δεν επιτρέπεται.");
        var before = Show(entity, reference);
        var count = entity switch
        {
            "customers" => db.Scalar("SELECT COUNT(*) FROM policies WHERE customer_id=$id", ("$id", before.Id)),
            "insurers" => db.Scalar("SELECT COUNT(*) FROM policies WHERE insurer_id=$id", ("$id", before.Id)),
            "partners" => db.Scalar("SELECT COUNT(*) FROM policies WHERE partner_id=$id", ("$id", before.Id)),
            _ => db.Scalar("SELECT (SELECT COUNT(*) FROM receipts WHERE policy_id=$id)+(SELECT COUNT(*) FROM credits WHERE policy_id=$id)+(SELECT COUNT(*) FROM policies WHERE previous_id=$id OR (id=$id AND status='renewed'))", ("$id", before.Id))
        };
        if (Convert.ToInt64(count) > 0) throw new UserError("Η διαγραφή δεν επιτρέπεται: υπάρχουν συνδεδεμένα δεδομένα.");
        db.Execute($"DELETE FROM {entity} WHERE id=$id", ("$id", before.Id)); db.Audit(entity, before.Id, "delete", before, null); return 0;
    });

    public Row VoidReceipt(string reference, string reason) => db.Atomic(() =>
    {
        var before = Show("receipts", reference);
        if (before.Text("status") == "void") throw new UserError("Η είσπραξη είναι ήδη ακυρωμένη.");
        var text = Validation.Text(reason, "Αιτία ακύρωσης", 500, true);
        db.Update("receipts", before.Id, new() { ["status"] = "void", ["reason"] = text });
        var after = Show("receipts", reference); db.Audit("receipts", before.Id, "void", before, after); return after;
    });

    public Row VoidCredit(string reference, string reason) => db.Atomic(() =>
    {
        var before = Show("credits", reference);
        if (before.Text("status") == "void") throw new UserError("Το πιστωτικό είναι ήδη ακυρωμένο.");
        db.Update("credits", before.Id, new() { ["status"] = "void", ["void_reason"] = Validation.Text(reason, "Αιτία ακύρωσης", 500, true) });
        var after = Show("credits", reference); db.Audit("credits", before.Id, "void", before, after); return after;
    });

    public Row CancelPolicy(string reference, string reason) => db.Atomic(() =>
    {
        var before = Show("policies", reference);
        if (before.Text("status") is "cancelled" or "renewed") throw new UserError("Το συμβόλαιο είναι ήδη ακυρωμένο ή ανανεωμένο.");
        var text = Validation.Text(reason, "Αιτία ακύρωσης", 500, true);
        db.Update("policies", before.Id, new() { ["status"] = "cancelled" });
        var after = Show("policies", reference); var audit = new Row(); foreach (var field in after) audit[field.Key] = field.Value; audit["reason"] = text;
        db.Audit("policies", before.Id, "cancel", before, audit); return after;
    });

    public Row Renew(string reference, IReadOnlyDictionary<string, string> options) => db.Atomic(() =>
    {
        var previous = Show("policies", reference);
        if (previous.Text("status") is "cancelled" or "renewed") throw new UserError("Δεν επιτρέπεται νέα ανανέωση ακυρωμένου ή ήδη ανανεωμένου συμβολαίου.");
        var fields = new Dictionary<string, string>
        {
            ["customer"] = previous.Text("customer_id"), ["insurer"] = previous.Text("insurer_id"), ["number"] = Need(options, "number"),
            ["start"] = Need(options, "start"), ["end"] = Need(options, "end"), ["gross"] = previous.Text("gross"), ["net"] = previous.Text("net"),
            ["currency"] = previous.Text("currency"), ["office-rate"] = previous.Text("office_rate"), ["partner-rate"] = previous.Text("partner_rate"),
            ["branch"] = previous.Text("branch"), ["plate"] = previous.Text("plate")
        };
        if (previous["partner_id"] is not null) fields["partner"] = previous.Text("partner_id");
        foreach (var key in new[] { "gross", "net", "office-rate", "partner-rate" }) if (options.TryGetValue(key, out var value)) fields[key] = value;
        if (string.CompareOrdinal(fields["start"], previous.Text("end")) <= 0) throw new UserError("Η ανανέωση πρέπει να ξεκινά μετά τη λήξη του προηγούμενου συμβολαίου.");
        var body = PolicyFields(fields, null); body["previous_id"] = previous.Id;
        var id = db.Insert("policies", body, "P"); db.Update("policies", previous.Id, new() { ["status"] = "renewed" });
        var result = Show("policies", id.ToString(CultureInfo.InvariantCulture));
        db.Audit("policies", previous.Id, "renewed", previous, Show("policies", reference));
        db.Audit("policies", id, "renewal", previous, result); return result;
    });

    public List<Row> History(string entity, string reference)
    {
        entity = Entity(entity); long id;
        try { id = Show(entity, reference).Id; }
        catch (UserError)
        {
            var numeric = long.TryParse(reference, out var parsed) ? parsed : -1;
            var found = db.Scalar("SELECT entity_id FROM events WHERE entity=$entity AND (entity_id=$id OR json_extract(before_json,'$.code')=$code COLLATE NOCASE OR json_extract(after_json,'$.code')=$code COLLATE NOCASE) ORDER BY id DESC LIMIT 1", ("$entity", entity), ("$id", numeric), ("$code", reference));
            if (found is null) throw new UserError("Δεν βρέθηκε ιστορικό για αυτόν τον κωδικό ή ID.");
            id = Convert.ToInt64(found);
        }
        return db.Query("SELECT id,action,occurred_at,before_json,after_json FROM events WHERE entity=$entity AND entity_id=$id ORDER BY id", ("$entity", entity), ("$id", id));
    }

    public List<Row> Summary()
    {
        var policies = db.Query("SELECT * FROM policy_records").Select(EnrichPolicy).ToList();
        return policies.GroupBy(row => row.Text("currency")).OrderBy(group => group.Key).Select(group =>
        {
            var production = group.Where(row => row.Text("status") != "cancelled").ToList();
            return new Row { ["currency"] = group.Key, ["policies"] = group.Count(), ["active_policies"] = group.Count(row => row.Text("status") == "active"),
                ["gross"] = production.Sum(row => row.Number("gross")), ["net"] = production.Sum(row => row.Number("net")),
                ["received"] = group.Sum(row => row.Number("received")), ["outstanding"] = production.Sum(row => row.Number("outstanding")),
                ["credited"] = group.Sum(row => row.Number("credited")), ["credited_net"] = group.Sum(row => row.Number("credited_net")),
                ["chargeable"] = production.Sum(row => row.Number("chargeable")), ["customer_credit"] = group.Sum(row => row.Number("customer_credit")),
                ["office_received"] = production.Sum(row => row.Number("office_received")) };
        }).ToList();
    }

    private Dictionary<string, object?> CustomerFields(IReadOnlyDictionary<string, string> options, Row? before)
    {
        string Value(string key, string fallback = "") => options.GetValueOrDefault(key) ?? before?.Text(key.Replace('-', '_')) ?? fallback;
        var kind = Value("kind", "person");
        if (kind is not ("person" or "company")) throw new UserError("Το --kind πρέπει να είναι person ή company.");
        var first = Validation.Text(Value("first-name"), "Όνομα", 100, kind == "person");
        var last = Validation.Text(Value("last-name"), "Επώνυμο", 100, kind == "person");
        var name = kind == "company" ? Validation.Text(Value("name"), "Επωνυμία", 200, true) : $"{first} {last}";
        return new() { ["kind"] = kind, ["first_name"] = first, ["last_name"] = last, ["name"] = name, ["vat"] = Validation.Vat(Value("vat")),
            ["phone"] = Validation.Phone(Value("phone")), ["email"] = Validation.Email(Value("email")), ["city"] = Validation.Text(Value("city"), "Πόλη", 120),
            ["code"] = before?.Text("code") ?? Validation.Text(Value("code"), "Κωδικός", 40) };
    }
    private static Dictionary<string, object?> PartyFields(IReadOnlyDictionary<string, string> options, bool partner, Row? before = null)
    {
        string Value(string key, string fallback = "") => options.GetValueOrDefault(key) ?? before?.Text(key) ?? fallback;
        var fields = new Dictionary<string, object?> { ["name"] = Validation.Text(Value("name"), "Ονομασία", 200, true), ["phone"] = Validation.Phone(Value("phone")), ["email"] = Validation.Email(Value("email")), ["code"] = before?.Text("code") ?? Validation.Text(Value("code"), "Κωδικός", 40) };
        if (partner) fields["commission_bps"] = Validation.Rate(Value("commission", "0"));
        return fields;
    }
    private Dictionary<string, object?> PolicyFields(IReadOnlyDictionary<string, string> options, Row? before)
    {
        string Value(string key, string fallback = "") => options.GetValueOrDefault(key) ?? before?.Text(key.Replace('-', '_')) ?? fallback;
        var customer = Show("customers", options.GetValueOrDefault("customer") ?? before?.Text("customer_id") ?? throw new UserError("Λείπει --customer."));
        var insurer = Show("insurers", options.GetValueOrDefault("insurer") ?? before?.Text("insurer_id") ?? throw new UserError("Λείπει --insurer."));
        var partnerReference = options.GetValueOrDefault("partner") ?? before?.Text("partner_id") ?? "";
        var partner = partnerReference.Length == 0 ? null : Show("partners", partnerReference);
        var gross = Validation.Money(Value("gross"), true); var net = Validation.Money(Value("net"));
        if (net > gross) throw new UserError("Τα καθαρά ασφάλιστρα δεν επιτρέπεται να υπερβαίνουν τα μικτά.");
        var officeRate = Validation.Rate(options.GetValueOrDefault("office-rate") ?? before?.Text("office_rate") ?? "0");
        var partnerRate = Validation.Rate(options.GetValueOrDefault("partner-rate") ?? before?.Text("partner_rate") ?? (partner?.Text("commission") ?? "0"));
        if (officeRate + partnerRate > 10000 || partner is null && partnerRate > 0) throw new UserError("Μη έγκυρη κατανομή προμηθειών: απαιτείται συνεργάτης για προμήθεια.");
        var start = Validation.Date(Value("start")); var end = Validation.Date(Value("end"));
        if (string.CompareOrdinal(start, end) > 0) throw new UserError("Η λήξη πρέπει να είναι ίση ή μεταγενέστερη της έναρξης.");
        var currency = Validation.Currency(Value("currency", "EUR"));
        if (before is not null && Convert.ToInt64(db.Scalar("SELECT (SELECT COUNT(*) FROM receipts WHERE policy_id=$id)+(SELECT COUNT(*) FROM credits WHERE policy_id=$id)", ("$id", before.Id))) > 0
            && (customer.Id != Convert.ToInt64(before["customer_id"]) || insurer.Id != Convert.ToInt64(before["insurer_id"]) || currency != before.Text("currency")
                || !Equals(partner?.Id, before["partner_id"]) || gross / 100m < before.Number("gross") && gross / 100m < before.Number("received") + before.Number("credited")
                || before.Number("credited") > gross / 100m || before.Number("credited_net") > net / 100m
                || net / 100m - before.Number("credited_net") > gross / 100m - before.Number("credited")))
            throw new UserError("Οι καταχωρημένες εισπράξεις ή τα πιστωτικά δεν επιτρέπουν αυτή την αλλαγή μερών, νομίσματος ή συνδέσεων.");
        return new() { ["number"] = Validation.Text(Value("number"), "Αριθμός συμβολαίου", 80, true), ["customer_id"] = customer.Id, ["insurer_id"] = insurer.Id,
            ["partner_id"] = partner?.Id, ["branch"] = Validation.Text(Value("branch"), "Κλάδος", 100), ["plate"] = Validation.Text(Value("plate"), "Πινακίδα", 20),
            ["start"] = start, ["end"] = end, ["gross_cents"] = gross, ["net_cents"] = net, ["currency"] = currency, ["office_bps"] = officeRate, ["partner_bps"] = partnerRate,
            ["code"] = before?.Text("code") ?? Validation.Text(Value("code"), "Κωδικός", 40) };
    }
    private Dictionary<string, object?> ReceiptFields(IReadOnlyDictionary<string, string> options)
    {
        var policy = Show("policies", Need(options, "policy")); var amount = Validation.Money(Need(options, "amount"), true);
        if (policy.Text("status") == "cancelled") throw new UserError("Δεν καταχωρείται νέα είσπραξη σε ακυρωμένο συμβόλαιο.");
        if (amount / 100m > policy.Number("outstanding")) throw new UserError("Η είσπραξη υπερβαίνει το ανεξόφλητο ποσό του συμβολαίου.");
        var method = options.GetValueOrDefault("method", "bank");
        if (method is not ("cash" or "bank" or "card" or "other")) throw new UserError("Μέθοδος: cash, bank, card ή other.");
        return new() { ["code"] = Validation.Text(options.GetValueOrDefault("code"), "Κωδικός", 40), ["policy_id"] = policy.Id, ["amount_cents"] = amount, ["date"] = Validation.Date(options.GetValueOrDefault("date", today.ToString("yyyy-MM-dd"))),
            ["status"] = "posted", ["method"] = method, ["number"] = Validation.Text(options.GetValueOrDefault("number", "R-" + Guid.NewGuid().ToString("N")[..12]), "Αριθμός είσπραξης", 40, true) };
    }
    private Row EnrichPolicy(Row row)
    {
        if (row.Text("status") == "active") row["status"] = string.CompareOrdinal(row.Text("end"), today.ToString("yyyy-MM-dd")) < 0 ? "expired" : string.CompareOrdinal(row.Text("start"), today.ToString("yyyy-MM-dd")) > 0 ? "scheduled" : "active";
        row["chargeable"] = row.Number("gross") - row.Number("credited");
        row["chargeable_net"] = row.Number("net") - row.Number("credited_net");
        row["outstanding"] = Math.Max(0, row.Number("chargeable") - row.Number("received"));
        row["customer_credit"] = Math.Max(0, row.Number("received") - row.Number("chargeable"));
        row["office_expected"] = Math.Round(row.Number("chargeable_net") * row.Number("office_rate") / 100m, 2, MidpointRounding.AwayFromZero);
        row["partner_expected"] = Math.Round(row.Number("chargeable_net") * row.Number("partner_rate") / 100m, 2, MidpointRounding.AwayFromZero);
        row["office_received"] = row.Number("chargeable") == 0 ? 0m : Math.Round(row.Number("office_expected") * Math.Clamp(row.Number("received") / row.Number("chargeable"), 0m, 1m), 2, MidpointRounding.AwayFromZero);
        return row;
    }

    private Dictionary<string, object?> CreditFields(IReadOnlyDictionary<string, string> options)
    {
        var policy = Show("policies", Need(options, "policy"));
        var amount = Validation.Money(Need(options, "amount"), true);
        var available = policy.Number("gross") - policy.Number("credited");
        if (amount / 100m > available) throw new UserError("Τα πιστωτικά δεν επιτρέπεται να υπερβαίνουν τα αρχικά μικτά ασφάλιστρα.");
        var availableNet = policy.Number("net") - policy.Number("credited_net");
        var net = options.TryGetValue("net", out var raw) ? Validation.Money(raw) / 100m
            : amount / 100m == available ? availableNet : Math.Round(amount / 100m * policy.Number("net") / policy.Number("gross"), 2, MidpointRounding.AwayFromZero);
        if (net > amount / 100m || net > availableNet || availableNet - net > available - amount / 100m)
            throw new UserError("Μη έγκυρα καθαρά πιστωτικού: ελέγξτε τα διαθέσιμα μικτά και καθαρά ασφάλιστρα.");
        return new() { ["code"] = Validation.Text(options.GetValueOrDefault("code"), "Κωδικός", 40), ["policy_id"] = policy.Id,
            ["amount_cents"] = amount, ["net_cents"] = checked((long)(net * 100)),
            ["date"] = Validation.Date(options.GetValueOrDefault("date", today.ToString("yyyy-MM-dd"))),
            ["number"] = Validation.Text(options.GetValueOrDefault("number", "CN-" + Guid.NewGuid().ToString("N")[..12]), "Αριθμός πιστωτικού", 40, true),
            ["reason"] = Validation.Text(Need(options, "reason"), "Αιτία πιστωτικού", 500, true) };
    }
}
