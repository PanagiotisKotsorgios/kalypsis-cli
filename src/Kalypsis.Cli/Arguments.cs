using Kalypsis.Cli.Core;

namespace Kalypsis.Cli;

public sealed record Arguments(List<string> Words, Dictionary<string, string> Options)
{
    private static readonly string[] Flags = ["help", "version", "json", "yes"];
    public static Arguments Parse(string[] args)
    {
        var words = new List<string>(); var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "-h" or "-?" or "/?") arg = "--help";
            if (!arg.StartsWith("--", StringComparison.Ordinal)) { words.Add(arg); continue; }
            var pair = arg[2..].Split('=', 2); var key = pair[0]; string value;
            if (Flags.Contains(key))
            {
                if (pair.Length > 1) throw new UserError($"Η επιλογή --{key} δεν δέχεται τιμή.");
                value = "true";
            }
            else if (pair.Length > 1) value = pair[1];
            else
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal)) throw new UserError($"Λείπει τιμή για --{key}.");
                value = args[++index];
            }
            if (!options.TryAdd(key, value)) throw new UserError($"Η επιλογή --{key} δόθηκε περισσότερες από μία φορές.");
        }
        return new(words, options);
    }

    public void Validate(IEnumerable<string> allowed, int wordCount)
    {
        var accepted = allowed.Concat(["db", "json", "help", "version"]).ToHashSet(StringComparer.Ordinal);
        foreach (var key in Options.Keys) if (!accepted.Contains(key)) throw new UserError($"Μη υποστηριζόμενη επιλογή --{key} για αυτή την εντολή.");
        if (Words.Count != wordCount) throw new UserError("Μη έγκυρη σύνταξη. Εκτελέστε kalypsis help.");
    }

    public static string[] Fields(string entity) => entity switch
    {
        "customers" => ["kind", "first-name", "last-name", "name", "vat", "email", "phone", "city"],
        "insurers" => ["name", "phone", "email"], "partners" => ["name", "phone", "email", "commission"],
        "policies" => ["number", "customer", "insurer", "partner", "branch", "plate", "start", "end", "gross", "net", "currency", "office-rate", "partner-rate"],
        "receipts" => ["number", "policy", "date", "amount", "method"], _ => []
    };
    public static string[] Filters(string entity) => entity switch
    {
        "policies" => ["search", "customer", "insurer", "partner", "from", "to", "status", "limit"],
        "receipts" => ["search", "policy", "from", "to", "status", "limit"], _ => ["search", "limit"]
    };
}
