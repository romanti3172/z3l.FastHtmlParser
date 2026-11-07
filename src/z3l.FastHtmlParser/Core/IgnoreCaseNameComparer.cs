namespace z3l.FastHtmlParser.Core;

/// <summary>
/// Case-insensitive comparer for short ASCII names (tag names), usable with span lookups.
/// Gives exactly the results of <see cref="StringComparison.OrdinalIgnoreCase"/>, but:
/// <list type="bullet">
/// <item>equality has an ASCII fast path (non-ASCII input falls back to OrdinalIgnoreCase);</item>
/// <item>the hash uses only the length and three upper-cased chars instead of a randomized hash of the whole name.</item>
/// </list>
/// A Dictionary with it is built in ~2 ms on first use (a FrozenDictionary took ~16 ms to JIT)
/// and looks tag names up ~30% faster than a FrozenDictionary.
/// </summary>
internal sealed class IgnoreCaseNameComparer : IEqualityComparer<string>, IAlternateEqualityComparer<ReadOnlySpan<char>, string>
{
    public static readonly IgnoreCaseNameComparer Instance = new();

    public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(string obj) => Hash(obj);

    public bool Equals(ReadOnlySpan<char> alternate, string other) => EqualsIgnoreCase(alternate, other);

    public int GetHashCode(ReadOnlySpan<char> alternate) => Hash(alternate);

    public string Create(ReadOnlySpan<char> alternate) => alternate.ToString();

    private static bool EqualsIgnoreCase(ReadOnlySpan<char> a, string b)
    {
        if (a.Length != b.Length) return false;

        for (int i = 0; i < a.Length; i++)
        {
            char x = a[i], y = b[i];
            if (x == y) continue;

            // non-ASCII: exact OrdinalIgnoreCase semantics
            if ((x | y) >= 0x80) return a.Equals(b, StringComparison.OrdinalIgnoreCase);

            // ASCII: equal only as the same letter in different case
            uint lower = (uint)(x | 0x20);
            if (lower != (uint)(y | 0x20) || lower - 'a' > 'z' - 'a') return false;
        }

        return true;
    }

    // Names equal under OrdinalIgnoreCase have equal upper-cased chars, so they get equal hashes.
    private static int Hash(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty) return 0;
        return (int)(((uint)s.Length << 16) ^ (Upper(s[0]) << 8) ^ Upper(s[^1]) ^ (Upper(s[s.Length >> 1]) << 21));
    }

    private static uint Upper(char c) => c <= 'z'
        ? (uint)(c is >= 'a' and <= 'z' ? c - 0x20 : c)
        : char.ToUpperInvariant(c);
}
