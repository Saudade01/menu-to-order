using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MenuToOrder.Api.Shared;

public static partial class Slug
{
    public static string For(string name)
    {
        var ascii = string.Concat(name.ToLower(CultureInfo.GetCultureInfo("tr-TR")).Select(c => c switch
        {
            'ç' => 'c', 'ğ' => 'g', 'ı' => 'i', 'ö' => 'o', 'ş' => 's', 'ü' => 'u', 'â' => 'a', 'î' => 'i', 'û' => 'u',
            _ => c,
        }));
        var core = NotAlphanumeric().Replace(ascii, "-").Trim('-');
        if (core.Length > 40)
            core = core[..40].TrimEnd('-');

        // A random tail keeps slugs unique without a lookup, even for two restaurants with the same name.
        return $"{(core.Length == 0 ? "menu" : core)}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3))}";
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotAlphanumeric();
}
