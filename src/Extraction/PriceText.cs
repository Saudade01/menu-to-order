using System.Globalization;
using System.Text.RegularExpressions;

namespace MenuToOrder.Extraction;

public enum Currency { Lira, Other, Missing }

public sealed record ParsedPrice(decimal? Amount, Currency Currency);

// Turns a price as printed ("1.250,00 TL", "95 ₺") into an amount. Anything ambiguous
// comes back without an amount so it gets flagged; a guessed price is worse than none.
public static partial class PriceText
{
    public static ParsedPrice Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new(null, Currency.Missing);

        var currency = Lira().IsMatch(text) ? Currency.Lira
            : Foreign().IsMatch(text) ? Currency.Other
            : Currency.Missing;

        var numbers = Number().Matches(text);
        return new(numbers.Count == 1 ? Amount(numbers[0].Value) : null, currency);
    }

    private static decimal? Amount(string token)
    {
        var separators = token.Where(c => c is '.' or ',').ToArray();
        var groups = token.Split('.', ',');

        var fraction = "";
        if (separators.Length > 0 && groups[^1].Length <= 2)
        {
            fraction = groups[^1];
            groups = groups[..^1];
            separators = separators[..^1];
        }

        // Remaining separators can only be thousands marks. In Turkish that is the dot;
        // "1,250" could mean 1.25 or 1250, so a comma here is not trusted.
        if (separators.Any(c => c == ','))
            return null;
        if (groups.Length > 1 && (groups[0].Length > 3 || groups[1..].Any(g => g.Length != 3)))
            return null;

        var whole = string.Concat(groups);
        return decimal.TryParse(fraction.Length == 0 ? whole : $"{whole}.{fraction}",
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ? amount : null;
    }

    [GeneratedRegex(@"₺|(?<!\p{L})(TL|TRY)(?!\p{L})", RegexOptions.IgnoreCase)]
    private static partial Regex Lira();

    [GeneratedRegex(@"[$€£]|(?<!\p{L})(USD|EUR|GBP)(?!\p{L})", RegexOptions.IgnoreCase)]
    private static partial Regex Foreign();

    [GeneratedRegex(@"\d+(?:[.,]\d+)*")]
    private static partial Regex Number();
}
