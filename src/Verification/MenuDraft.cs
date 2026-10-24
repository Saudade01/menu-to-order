using System.Globalization;
using System.Text.RegularExpressions;
using MenuToOrder.Data;
using MenuToOrder.Extraction;

namespace MenuToOrder.Verification;

public sealed record MenuDraft(MenuVersion Version, IReadOnlyList<MenuItem> Items, IReadOnlyList<Flag> Flags, IReadOnlyList<ExtractionRun> Runs);

// Merges the two passes into one draft and flags every field that does not clear the threshold.
public static partial class Drafts
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static MenuDraft Build(Guid restaurantId, MenuRead first, MenuRead second, VerificationOptions options, DateTimeOffset now)
    {
        var version = new MenuVersion(restaurantId, now);
        var runs = new[] { Run(version, first), Run(version, second) };
        var pairs = Pair(first.Menu?.Items ?? [], second.Menu?.Items ?? []);

        var detected = (first.Menu?.Categories ?? []).Concat(second.Menu?.Categories ?? []).Select(Key).ToHashSet();
        var nameCounts = pairs.Select(p => (p.First ?? p.Second)!).CountBy(v => (Key(v.Category), Key(v.Name))).ToDictionary();

        var items = new List<MenuItem>();
        var flags = new List<Flag>();
        foreach (var (a, b) in pairs)
        {
            var v = a ?? b!;
            var price = PriceText.Parse(v.PriceText);
            var item = new MenuItem(version.Id, items.Count, string.IsNullOrWhiteSpace(v.Name) ? "(no name)" : v.Name.Trim(),
                v.Category, v.Description, price.Amount);
            items.Add(item);

            foreach (var field in Enum.GetValues<MenuField>())
            {
                var rules = Rules(field, v, price, detected, nameCounts, options).ToList();
                var agree = a is not null && b is not null && Same(field, a, b);
                var legible = !Unclear(a, field) && !Unclear(b, field) && !(field == MenuField.Name && string.IsNullOrWhiteSpace(v.Name));

                var score = options.AgreementWeight * (agree ? 1 : 0)
                    + options.RulesWeight * (rules.Count == 0 ? 1 : 0)
                    + options.LegibilityWeight * (legible ? 1 : 0);
                if (score >= options.PublishThreshold)
                    continue;

                var reasons = new List<FlagReason>();
                if (!agree)
                    reasons.Add(FlagReason.PassesDisagree);
                reasons.AddRange(rules);
                if (!legible)
                    reasons.Add(FlagReason.LowLegibility);

                flags.AddRange(reasons.Select(r =>
                    new Flag(item.Id, field, r, Text(a, field), Text(b, field), Math.Clamp(score, 0, 1))));
            }
        }

        return new MenuDraft(version, items, flags, runs);
    }

    private static IEnumerable<FlagReason> Rules(MenuField field, ExtractedItem v, ParsedPrice price, HashSet<string> detected,
        Dictionary<(string, string), int> nameCounts, VerificationOptions options)
    {
        switch (field)
        {
            case MenuField.Price when price.Amount is null:
                yield return FlagReason.PriceUnreadable;
                break;
            case MenuField.Price:
                if (price.Amount < options.MinPrice || price.Amount > options.MaxPrice)
                    yield return FlagReason.PriceOutOfRange;
                if (price.Currency == Currency.Missing)
                    yield return FlagReason.CurrencyMissing;
                if (price.Currency == Currency.Other)
                    yield return FlagReason.ForeignCurrency;
                break;
            case MenuField.Name when nameCounts[(Key(v.Category), Key(v.Name))] > 1:
                yield return FlagReason.DuplicateName;
                break;
            case MenuField.Category when v.Category is null || !detected.Contains(Key(v.Category)):
                yield return FlagReason.UnknownCategory;
                break;
        }
    }

    // Lines items up by category, name and how many times that pair has appeared so far,
    // so two "Köfte" rows in one section stay two items instead of collapsing into one.
    private static List<(ExtractedItem? First, ExtractedItem? Second)> Pair(IReadOnlyList<ExtractedItem> first, IReadOnlyList<ExtractedItem> second)
    {
        var unmatched = Keyed(second).ToDictionary(x => x.Key, x => x.Item);
        var pairs = new List<(ExtractedItem? First, ExtractedItem? Second)>();
        foreach (var (key, item) in Keyed(first))
            pairs.Add((item, unmatched.Remove(key, out var match) ? match : null));
        pairs.AddRange(Keyed(second).Where(x => unmatched.ContainsKey(x.Key)).Select(x => ((ExtractedItem?)null, (ExtractedItem?)x.Item)));
        return pairs;
    }

    private static IEnumerable<((string, string, int) Key, ExtractedItem Item)> Keyed(IReadOnlyList<ExtractedItem> items)
    {
        var seen = new Dictionary<(string, string), int>();
        foreach (var item in items)
        {
            var k = (Key(item.Category), Key(item.Name));
            seen[k] = seen.GetValueOrDefault(k) + 1;
            yield return ((k.Item1, k.Item2, seen[k]), item);
        }
    }

    private static bool Same(MenuField field, ExtractedItem a, ExtractedItem b)
    {
        if (field != MenuField.Price)
            return Key(Text(a, field)) == Key(Text(b, field));

        var (pa, pb) = (PriceText.Parse(a.PriceText), PriceText.Parse(b.PriceText));
        return pa.Amount is not null || pb.Amount is not null ? pa == pb : Key(a.PriceText) == Key(b.PriceText);
    }

    private static bool Unclear(ExtractedItem? item, MenuField field) =>
        item?.Unclear.Contains(field.ToString(), StringComparer.OrdinalIgnoreCase) == true;

    private static string? Text(ExtractedItem? item, MenuField field) => field switch
    {
        MenuField.Name => item?.Name,
        MenuField.Category => item?.Category,
        MenuField.Description => item?.Description,
        _ => item?.PriceText,
    };

    private static string Key(string? s) => Whitespace().Replace(s?.Trim() ?? "", " ").ToUpper(Turkish);

    private static ExtractionRun Run(MenuVersion version, MenuRead read) => new(
        version.Id, read.Pass, read.PromptVersion, read.Model, read.InputHash, read.RawResponse, read.Error,
        read.Duration, read.InputTokens, read.OutputTokens, read.StartedAt);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
