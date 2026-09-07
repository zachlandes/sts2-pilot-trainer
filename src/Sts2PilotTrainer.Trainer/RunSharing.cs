using System.Security.Cryptography;
using System.Text;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>The only personal value attached to a shared run.</summary>
public sealed record ShareSubmission(
    string Name,
    string Description,
    string DisplayName,
    bool Cc0Consent);

/// <summary>An immutable, content-addressed run accepted by the sharing service.</summary>
public sealed record SharedRun(
    string ShareId,
    string Code,
    string ManifestJson,
    ShareSubmission Submission,
    LibraryRun Run,
    DateTimeOffset SubmittedAt,
    bool Featured = false);

public sealed class ShareValidationException(string message) : Exception(message);

/// <summary>
/// The narrow boundary between the run library and a sharing transport.
/// Implementations may be HTTP-backed; the deterministic implementation below is the
/// local service used to prove submission through retrieval without credentials.
/// </summary>
public interface IRunSharingApi
{
    SharedRun Submit(string manifestJson, ShareSubmission submission);
    IReadOnlyList<SharedRun> Index();
    SharedRun? Find(string code);
}

/// <summary>
/// Deterministic local sharing service. The publication gate is supplied by the host,
/// so no transport can turn an unchecked recording into a shared one.
/// </summary>
public sealed class LocalRunSharingService(
    Func<ReplayManifest, bool> publicationGate,
    Func<ReplayManifest, LibraryRun> describe,
    Func<DateTimeOffset>? clock = null,
    Func<ReplayManifest, bool>? featured = null) : IRunSharingApi
{
    private readonly Dictionary<string, SharedRun> shared = new(StringComparer.Ordinal);

    public SharedRun Submit(string manifestJson, ShareSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ValidateSubmission(submission);

        ReplayManifest manifest;
        try
        {
            manifest = ManifestJson.Deserialize(manifestJson);
        }
        catch (Exception ex)
        {
            throw new ShareValidationException($"The run is not a valid replay manifest: {ex.Message}");
        }

        if (!publicationGate(manifest))
            throw new ShareValidationException("Local validation did not pass, so the run was not sent.");

        var canonical = ManifestJson.Serialize(manifest);
        var identityInput = Encoding.UTF8.GetBytes(canonical + "\n" + submission.Name + "\n" +
            submission.Description + "\n" + submission.DisplayName);
        var hash = Convert.ToHexString(SHA256.HashData(identityInput)).ToLowerInvariant();
        var code = hash[..12].ToUpperInvariant();

        if (shared.TryGetValue(hash, out var existing)) return existing;

        var result = new SharedRun(
            hash, code, canonical, submission, describe(manifest),
            (clock ?? (() => DateTimeOffset.UtcNow))(), featured?.Invoke(manifest) == true);
        shared.Add(hash, result);
        return result;
    }

    public IReadOnlyList<SharedRun> Index() =>
    [
        .. shared.Values.Where(run => run.Featured),
        .. shared.Values.Where(run => !run.Featured)
            .OrderByDescending(run => run.SubmittedAt)
            .ThenBy(run => run.ShareId, StringComparer.Ordinal),
    ];

    public SharedRun? Find(string code)
    {
        var wanted = code?.Trim();
        if (string.IsNullOrEmpty(wanted)) return null;
        return shared.Values.SingleOrDefault(run =>
            string.Equals(run.Code, wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateSubmission(ShareSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.Name) || submission.Name.Length > 40)
            throw new ShareValidationException("Name is required and may contain at most 40 characters.");
        if (submission.Description.Length > 200)
            throw new ShareValidationException("Description may contain at most 200 characters.");
        if (string.IsNullOrWhiteSpace(submission.DisplayName))
            throw new ShareValidationException("Display name is required for submission.");
        if (!submission.Cc0Consent)
            throw new ShareValidationException("CC0 consent is required before submission.");
    }
}

/// <summary>The designed browser state, including the exceptional exact-code path.</summary>
public sealed record SharingBrowser(
    LibraryTab Tab,
    bool CompatibleOnly,
    IReadOnlyList<BrowserGroup> Groups,
    string? SelectedCode,
    RunLookup? Selected)
{
    public static SharingBrowser For(
        LibraryTab tab, IReadOnlyList<SharedRun> index, string thisBuild,
        bool compatibleOnly = true, string? selectedCode = null)
    {
        var runs = index.Select(item => item.Run with
        {
            Origin = item.Featured ? RunOrigin.Featured : RunOrigin.Recent,
        }).ToList();
        var selected = selectedCode is null
            ? null
            : index.FirstOrDefault(item => string.Equals(item.Code, selectedCode.Trim(),
                StringComparison.OrdinalIgnoreCase));
        if (selected is not null && !selected.Run.Listed) compatibleOnly = false;

        var visible = compatibleOnly ? runs.Where(run => run.Listed).ToList() : runs;
        var inTab = visible.Where(run => (run.Origin == RunOrigin.Mine) == (tab == LibraryTab.MyRuns)).ToList();
        IReadOnlyList<BrowserGroup> groups = tab == LibraryTab.MyRuns
            ? (inTab.Count == 0 ? [] : [new BrowserGroup(null, Newest(inTab))])
            :
            [
                .. Group(LibraryCopy.FeaturedGroup, inTab.Where(run => run.Origin == RunOrigin.Featured).ToList()),
                .. Group(LibraryCopy.RecentGroup, Newest(inTab.Where(run => run.Origin == RunOrigin.Recent))),
            ];
        var lookup = selected is null ? null : RunBrowser.Lookup(selected.Run.RunId, runs, thisBuild);
        return new SharingBrowser(tab, compatibleOnly, groups, selected?.Code, lookup);
    }

    private static IReadOnlyList<BrowserGroup> Group(string heading, IReadOnlyList<LibraryRun> runs) =>
        runs.Count == 0 ? [] : [new BrowserGroup(heading, runs)];

    private static IReadOnlyList<LibraryRun> Newest(IEnumerable<LibraryRun> runs) =>
        [.. runs.OrderByDescending(run => run.Recorded ?? DateTimeOffset.MinValue)
            .ThenBy(run => run.RunId, StringComparer.Ordinal)];
}

/// <summary>All fixed submission wording shown in one popup.</summary>
public static class SharingCopy
{
    public const string ShareThisRun = "Share this run";
    public const string OthersTab = "Others";
    public const string MineTab = "Mine";
    public const string CompatibleFilter = "Compatible with your game version";
    public const string FetchRunIndex = "Fetch the run index";
    public const string Privacy = "No other personal information travels with this run.";
    public const string Consent = "I release this run under CC0.";
    public const string LocalValidation = "Validation runs locally before anything is sent.";
}
