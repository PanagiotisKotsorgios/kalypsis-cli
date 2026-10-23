using System.Globalization;
using System.Text.RegularExpressions;

namespace Kalypsis.Cli.Core;

public sealed class UserError(string message) : Exception(message);

public static partial class Validation
{
    public static string Text(string? value, string label, int maximum = 200, bool required = false)
    {
        var text = value?.Trim() ?? "";
        if (required && text.Length == 0) throw new UserError($"Συμπληρώστε: {label}.");
        if (text.Length > maximum || text.Any(char.IsControl)) throw new UserError($"Μη έγκυρο πεδίο «{label}» (έως {maximum} χαρακτήρες).");
        return text;
    }

    public static string Email(string? value)
    {
        var text = Text(value, "Email", 160);
        if (text.Length > 0 && !EmailPattern().IsMatch(text)) throw new UserError("Συμπληρώστε έγκυρο email.");
        return text;
    }

    public static string Phone(string? value)
    {
        var text = Text(value, "Τηλέφωνο", 40);
        if (text.Length > 0 && (!PhonePattern().IsMatch(text) || text.Count(char.IsAsciiDigit) is < 7 or > 15))
            throw new UserError("Το τηλέφωνο πρέπει να έχει 7–15 ψηφία, με προαιρετικό διεθνή κωδικό.");
        return text;
    }

    public static string Vat(string? value)
    {
        var text = Text(value, "ΑΦΜ", 9);
        if (text.Length > 0 && (text.Length != 9 || !text.All(char.IsAsciiDigit))) throw new UserError("Το ΑΦΜ πρέπει να έχει 9 ψηφία.");
        return text;
    }

    public static long Money(string value, bool positive = false)
    {
        if (!MoneyPattern().IsMatch(value) || !decimal.TryParse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            || amount > 9_999_999_999.99m || positive && amount <= 0)
            throw new UserError("Μη έγκυρο ποσό. Χρησιμοποιήστε π.χ. 100.50, με έως 2 δεκαδικά και χωρίς διαχωριστικά χιλιάδων.");
        return decimal.ToInt64(amount * 100m);
    }

    public static int Rate(string value)
    {
        var basisPoints = Money(value);
        if (basisPoints > 10_000) throw new UserError("Το ποσοστό πρέπει να είναι από 0 έως 100.");
        return (int)basisPoints;
    }

    public static string Date(string value)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new UserError("Η ημερομηνία πρέπει να έχει μορφή yyyy-MM-dd.");
        return value;
    }

    public static string Currency(string value)
    {
        if (value.Length != 3 || !value.All(char.IsAsciiLetter)) throw new UserError("Το νόμισμα πρέπει να έχει 3 γράμματα, π.χ. EUR.");
        return value.ToUpperInvariant();
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)] private static partial Regex EmailPattern();
    [GeneratedRegex(@"^\+?[0-9 ()\-]+$", RegexOptions.CultureInvariant)] private static partial Regex PhonePattern();
    [GeneratedRegex(@"^[0-9]+([.,][0-9]{1,2})?$", RegexOptions.CultureInvariant)] private static partial Regex MoneyPattern();
}
