using System.Globalization;
using System.Text;

namespace FullscreenStart.Services;

/// <summary>Ranks app names against a typed query (prefix &gt; word start &gt; initials &gt; contains &gt; fuzzy).</summary>
public static class SearchScorer
{
    public static int Score(string name, string query)
    {
        var n = Normalize(name);
        var q = Normalize(query).Trim();
        if (q.Length == 0 || n.Length == 0) return 0;

        if (n == q) return 1000;
        if (n.StartsWith(q, StringComparison.Ordinal)) return 900 - Math.Min(n.Length - q.Length, 99);

        var words = SplitWords(n);
        var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        bool allWordStart = tokens.All(t => words.Any(w => w.StartsWith(t, StringComparison.Ordinal)));
        if (allWordStart) return 700 - Math.Min(n.Length, 99);

        if (tokens.Length == 1 && q.Length >= 2)
        {
            var initials = string.Concat(words.Select(w => w[0]));
            if (initials.StartsWith(q, StringComparison.Ordinal)) return 600 - Math.Min(n.Length, 99);
        }

        if (tokens.All(t => n.Contains(t, StringComparison.Ordinal))) return 400 - Math.Min(n.Length, 99);

        var compact = q.Replace(" ", "");
        if (compact.Length >= 3 && IsSubsequence(n, compact)) return 150 - Math.Min(n.Length, 99);

        return 0;
    }

    public static List<T> Rank<T>(IEnumerable<T> items, Func<T, string> nameOf, string query, int max = 50)
    {
        return items
            .Select(item => (item, name: nameOf(item), score: Score(nameOf(item), query)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.name.Length)
            .ThenBy(x => x.name, StringComparer.CurrentCultureIgnoreCase)
            .Take(max)
            .Select(x => x.item)
            .ToList();
    }

    public static string Normalize(string s)
    {
        var decomposed = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string[] SplitWords(string s)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c)) current.Append(c);
            else if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
        }
        if (current.Length > 0) words.Add(current.ToString());
        return words.ToArray();
    }

    private static bool IsSubsequence(string haystack, string needle)
    {
        int i = 0;
        foreach (var c in haystack)
        {
            if (c == needle[i] && ++i == needle.Length) return true;
        }
        return false;
    }
}
