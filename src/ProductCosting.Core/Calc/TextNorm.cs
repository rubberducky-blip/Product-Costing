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

/// <summary>Orders "A2" before "A10", ignoring case and accents. Allocation-free, for sorting large lists fast.</summary>
public sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    // every UTF-16 char folded once: upper case, accents removed, final sigma as sigma
    static readonly char[] Fold = BuildFold();

    static char[] BuildFold()
    {
        var map = new char[char.MaxValue + 1];
        for (var i = 0; i <= char.MaxValue; i++)
        {
            var c = (char)i;
            // accents only matter for Latin, Greek and Cyrillic letters (below U+0500)
            if (i >= 0x0500) { map[i] = char.IsSurrogate(c) ? c : char.ToUpperInvariant(c); continue; }
            var d = c.ToString().Normalize(NormalizationForm.FormD);
            var b = d.Length > 0 ? d[0] : c;
            if (b == 'ς') b = 'σ';
            map[i] = char.ToUpperInvariant(b);
        }
        return map;
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                // compare whole numbers: skip leading zeros, then the longer number is bigger
                while (i < x.Length - 1 && x[i] == '0' && char.IsAsciiDigit(x[i + 1])) i++;
                while (j < y.Length - 1 && y[j] == '0' && char.IsAsciiDigit(y[j + 1])) j++;
                int si = i, sj = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                if (i - si != j - sj) return (i - si).CompareTo(j - sj);
                var c = x.AsSpan(si, i - si).SequenceCompareTo(y.AsSpan(sj, j - sj));
                if (c != 0) return c;
            }
            else
            {
                var a = Fold[x[i]];
                var b = Fold[y[j]];
                if (a != b) return a.CompareTo(b);
                i++;
                j++;
            }
        }
        var rest = (x.Length - i).CompareTo(y.Length - j);
        return rest != 0 ? rest : string.CompareOrdinal(x, y);
    }
}
