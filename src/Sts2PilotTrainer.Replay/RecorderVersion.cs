namespace Sts2PilotTrainer.Replay;

/// <summary>
/// A version as Runmobile's mod manifest spells one, ordered the way a release
/// candidate and its release order: <c>major.minor.patch</c>, with an optional
/// <c>-prerelease</c> of dot-separated identifiers, compared by semantic versioning's
/// precedence rule. <c>Directory.Build.props</c> permits a prerelease declaration and
/// refuses build metadata, so this is the one reader of what it lets through, and
/// <c>System.Version</c> is not: it cannot read <c>0.3.0-rc1</c>, and a candidate
/// whose own recordings it could not read would measure itself over none of them.
///
/// A prerelease orders below its release, and two prereleases order identifier by
/// identifier - a number below a number, a word below a word as text, a number below
/// any word, and the shorter list below the longer where every shared identifier
/// agrees - so <c>0.3.0-rc1 &lt; 0.3.0-rc2 &lt; 0.3.0</c>. Four numeric parts are not
/// a version here: that is .NET's default assembly version, which the manifest never
/// spells.
/// </summary>
public sealed record RecorderVersion(
    int Major, int Minor, int Patch, IReadOnlyList<string> Prerelease) : IComparable<RecorderVersion>
{
    public static RecorderVersion? TryParse(string? spelled)
    {
        if (string.IsNullOrEmpty(spelled)) return null;
        var dash = spelled.IndexOf('-');
        var core = dash < 0 ? spelled : spelled[..dash];
        var numbers = core.Split('.');
        if (numbers.Length != 3 || !numbers.All(IsNumericIdentifier)) return null;
        var prerelease = dash < 0 ? [] : spelled[(dash + 1)..].Split('.');
        if (prerelease.Any(identifier => identifier.Length == 0 || !identifier.All(IsIdentifierCharacter))) return null;
        if (prerelease.Any(identifier => identifier.All(char.IsAsciiDigit) && !IsNumericIdentifier(identifier))) return null;
        return new RecorderVersion(int.Parse(numbers[0]), int.Parse(numbers[1]), int.Parse(numbers[2]), prerelease);
    }

    public int CompareTo(RecorderVersion? other)
    {
        if (other is null) return 1;
        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0) return core;
        if (Prerelease.Count == 0 || other.Prerelease.Count == 0) return other.Prerelease.Count.CompareTo(Prerelease.Count);
        for (var i = 0; i < Math.Min(Prerelease.Count, other.Prerelease.Count); i++)
        {
            var identifier = CompareIdentifiers(Prerelease[i], other.Prerelease[i]);
            if (identifier != 0) return identifier;
        }
        return Prerelease.Count.CompareTo(other.Prerelease.Count);
    }

    public static bool operator <(RecorderVersion left, RecorderVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(RecorderVersion left, RecorderVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(RecorderVersion left, RecorderVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(RecorderVersion left, RecorderVersion right) => left.CompareTo(right) >= 0;

    public bool Equals(RecorderVersion? other) => other is not null && CompareTo(other) == 0;

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join(".", Prerelease));

    private static int CompareIdentifiers(string left, string right)
    {
        var leftNumeric = left.All(char.IsAsciiDigit);
        var rightNumeric = right.All(char.IsAsciiDigit);
        if (leftNumeric && rightNumeric) return int.Parse(left).CompareTo(int.Parse(right));
        if (leftNumeric != rightNumeric) return leftNumeric ? -1 : 1;
        return string.CompareOrdinal(left, right);
    }

    private static bool IsNumericIdentifier(string identifier) =>
        identifier.Length > 0
        && identifier.All(char.IsAsciiDigit)
        && (identifier.Length == 1 || identifier[0] != '0')
        && int.TryParse(identifier, out _);

    private static bool IsIdentifierCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c == '-';
}
