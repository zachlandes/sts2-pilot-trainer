namespace Sts2PilotTrainer.Replay;

/// <summary>One shipped build, and the day it became the current public beta.</summary>
public sealed record GameRelease(string Version, DateOnly ReleasedUtc);

/// <summary>
/// Which build a recording was made on, worked out from the day it was published.
///
/// This is a guess with a stated rule, never a fact: the recording is not the upload,
/// and nothing in a video has to say which build it ran on. It exists to give
/// <c>preflight</c> a candidate to test rather than to settle anything, and the whole
/// point of <see cref="Ambiguous"/> is that a guess which could be either of two
/// builds must say so instead of picking one.
/// </summary>
public sealed record VersionInference(
    string? Version,
    bool Ambiguous,
    string Reason,
    IReadOnlyList<string> Candidates)
{
    /// <summary>A single build the caller may proceed with.</summary>
    public bool IsResolved => Version is not null && !Ambiguous;
}

/// <summary>
/// The public beta's release dates, and the one rule this project uses to date a
/// recording against them.
///
/// The rule is the captain's, stated as: assume a recording used the latest beta
/// available, unless a patch landed the day of the upload or the day before, in which
/// case it could be either. That window exists because a run is played and edited
/// before it is published, so an upload on patch day says almost nothing about which
/// build the run itself was on.
///
/// The calendar is data, not a measurement, so it goes stale on its own schedule. It
/// is small on purpose: an entry earns its place by being needed to date a recording
/// this project actually ingests.
/// </summary>
public sealed class PatchCalendar
{
    /// <summary>
    /// How many days before an upload a release makes its own date ambiguous. One day,
    /// because that is the rule as given; widening it is a decision about how much
    /// editing latency to assume, not a bug fix.
    /// </summary>
    public const int AmbiguityWindowDays = 1;

    private readonly IReadOnlyList<GameRelease> _releases;

    public PatchCalendar(IEnumerable<GameRelease> releases)
    {
        var ordered = releases.OrderBy(release => release.ReleasedUtc).ToList();
        if (ordered.Count == 0)
        {
            throw new ManifestException("A patch calendar needs at least one release to date anything against.");
        }

        var duplicateVersions = ordered
            .GroupBy(release => release.Version, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateVersions.Count > 0)
        {
            throw new ManifestException(
                $"Patch calendar lists {string.Join(", ", duplicateVersions)} more than once. " +
                "A version with two release dates cannot date anything.");
        }

        _releases = ordered;
    }

    public IReadOnlyList<GameRelease> Releases => _releases;

    public GameRelease Latest => _releases[^1];

    public bool Knows(string version) =>
        _releases.Any(release => string.Equals(release.Version, version, StringComparison.Ordinal));

    /// <summary>
    /// The build a recording published on <paramref name="uploadedUtc"/> most likely used.
    ///
    /// Refuses rather than guesses when the upload predates everything the calendar
    /// knows, because an inference from an empty range is not a weaker answer, it is a
    /// made-up one.
    /// </summary>
    public VersionInference InferForUpload(DateOnly uploadedUtc)
    {
        var current = _releases.LastOrDefault(release => release.ReleasedUtc <= uploadedUtc);
        if (current is null)
        {
            return new VersionInference(
                null,
                Ambiguous: false,
                $"No release in this calendar had shipped by {uploadedUtc:yyyy-MM-dd}; the earliest it knows is " +
                $"{_releases[0].Version} on {_releases[0].ReleasedUtc:yyyy-MM-dd}. The build cannot be inferred.",
                []);
        }

        var daysSince = uploadedUtc.DayNumber - current.ReleasedUtc.DayNumber;
        if (daysSince > AmbiguityWindowDays)
        {
            return new VersionInference(
                current.Version,
                Ambiguous: false,
                $"{current.Version} shipped {current.ReleasedUtc:yyyy-MM-dd}, {daysSince} days before this upload, " +
                "and no later release had shipped. It was the current beta for the whole editing window.",
                [current.Version]);
        }

        var previous = _releases
            .LastOrDefault(release => release.ReleasedUtc < current.ReleasedUtc);
        if (previous is null)
        {
            return new VersionInference(
                current.Version,
                Ambiguous: false,
                $"{current.Version} shipped {current.ReleasedUtc:yyyy-MM-dd}, within {AmbiguityWindowDays} day(s) of " +
                "this upload, but the calendar knows no earlier release to confuse it with.",
                [current.Version]);
        }

        return new VersionInference(
            null,
            Ambiguous: true,
            $"{current.Version} shipped {current.ReleasedUtc:yyyy-MM-dd}, {daysSince} day(s) before this upload. " +
            "A run is played and edited before it is published, so this recording could be on either " +
            $"{previous.Version} or {current.Version}. Read the build off the recording or its description instead.",
            [previous.Version, current.Version]);
    }
}
