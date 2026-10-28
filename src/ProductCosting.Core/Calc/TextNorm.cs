using System.Globalization;
using System.Text;

namespace ProductCosting.Core.Calc;

/// <summary>Folds case and accents (Greek and Latin) so "Μπλε" matches "μπλέ".</summary>
public static class TextNorm
{
    public static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c == 'ς' ? 'σ' : char.IsWhiteSpace(c) ? ' ' : c);
        }
        return sb.ToString();
    }

    public static string Join(params string?[] parts) =>
        Normalize(string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p))));

    /// <summary>Normalized text safe to put inside a LIKE pattern.</summary>
    public static string LikePattern(string query) =>
        "%" + Normalize(query).Replace("%", "").Replace("_", "").Trim() + "%";
}

/// <summary>Orders "A2" before "A10".</summary>
public sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int si = i, sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                var a = x[si..i].TrimStart('0');
                var b = y[sj..j].TrimStart('0');
                if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                var c = string.CompareOrdinal(a, b);
                if (c != 0) return c;
            }
            else
            {
                var c = string.Compare(x[i].ToString(), y[j].ToString(), CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
