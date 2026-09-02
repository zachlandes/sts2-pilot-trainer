using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// What this machine's game actually is - read from the installation, never assumed.
///
/// Three independent readings, deliberately kept apart so a disagreement between
/// them is visible rather than averaged away:
///   - the installed game's own release file, read from disk without touching it;
///   - the prepared copy's bootstrap receipt, which records the install it came from;
///   - the engine in this process, which reports the content hash of the model
///     database it actually loaded.
/// </summary>
public sealed record GameIdentity(
    string BuildVersion,
    string BuildDateUtc,
    string Commit,
    string Branch,
    string ContentHash,
    string PristineAssemblySha256,
    IReadOnlyList<string> Notes)
{
    /// <summary>
    /// Reads the identity of the game this process would replay with.
    ///
    /// The content hash comes from the engine rather than from any file, because
    /// it is a property of the content that got loaded - which is exactly what a
    /// mod would change, and exactly what a file could not tell us.
    /// </summary>
    public static GameIdentity Read()
    {
        var notes = new List<string>();
        var libDir = AssemblyResolution.ResolveLibDirectory()
            ?? throw new EngineException(
                "No prepared game assembly found. Run ./scripts/bootstrap.sh, which copies your own " +
                "Steam install into build/lib without modifying it.");

        var receiptPath = Path.Combine(libDir, "prepared-assembly.json");
        if (!File.Exists(receiptPath))
        {
            throw new EngineException(
                $"build/lib has no prepared-assembly.json. The assembly copy is unattributed - refusing to " +
                "treat it as a known build. Re-run ./scripts/bootstrap.sh.");
        }

        var receipt = JsonNode.Parse(File.ReadAllText(receiptPath))!.AsObject();
        if (receipt["schema"]?.GetValue<string>() != "sts2-pilot-trainer/prepared-assembly/v2")
        {
            throw new EngineException(
                "The prepared assembly receipt predates output integrity hashes. Re-run ./scripts/bootstrap.sh.");
        }

        VerifyPreparedOutputs(libDir, receipt);
        var build = receipt["build"]!.AsObject();

        var contentHash = EngineHost.ContentHash();
        if (contentHash == "0")
        {
            throw new EngineException(
                "The engine reported content hash 0, which means the model database never initialised. " +
                "A hash over nothing is stable and meaningless - refusing to gate on it.");
        }

        var pristine = receipt["pristine_sts2_sha256"]!.GetValue<string>();
        foreach (var patch in receipt["il_patches"]!.AsArray())
        {
            notes.Add($"IL patch applied to the prepared copy: {patch!["name"]} ({patch["target"]})");
        }
        notes.Add($"engine registered {EngineHost.RegisteredModelCount()} models");

        return new GameIdentity(
            BuildVersion: build["version"]!.GetValue<string>(),
            BuildDateUtc: build["build_date_utc"]!.GetValue<string>(),
            Commit: build["commit"]!.GetValue<string>(),
            Branch: build["branch"]!.GetValue<string>(),
            ContentHash: contentHash,
            PristineAssemblySha256: pristine,
            Notes: notes);
    }

    /// <summary>
    /// The identity of the retail game this code is loaded inside.
    ///
    /// A different reading from <see cref="Read"/> rather than a fallback for it,
    /// and the difference is the point. <see cref="Read"/> answers from the prepared
    /// copy and its bootstrap receipt, which is the right answer for a headless host
    /// and a claim about no player's installation. Inside the client there is no
    /// receipt and no prepared copy: the build is whatever the running process says
    /// it is, and the assembly hashed is the one the process actually loaded.
    ///
    /// Only ever reads. The assembly file is opened for hashing and nothing else.
    /// </summary>
    public static GameIdentity ReadFromRunningGame()
    {
        if (EngineHost.Origin != EngineOrigin.RunningGame)
        {
            throw new EngineException(
                "This process has not adopted a running game, so there is no live build to read. Call " +
                "EngineHost.AdoptRunningGame first; it refuses rather than guessing.");
        }

        var releaseInfo = EngineHost.RunningGameReleaseInfo()
            ?? throw new EngineException("The running game published no release info.");

        var contentHash = EngineHost.ContentHash();
        if (contentHash == "0")
        {
            throw new EngineException(
                "The running game reported content hash 0, which means its id database never initialised. " +
                "A hash over nothing is stable and meaningless - refusing to gate on it.");
        }

        var assemblyPath = typeof(MegaCrit.Sts2.Core.Models.ModelDb).Assembly.Location;
        var notes = new List<string>
        {
            "identity read from the running game process, not from a prepared copy",
            $"engine registered {EngineHost.RegisteredModelCount()} models",
        };

        return new GameIdentity(
            BuildVersion: ReleaseInfoString(releaseInfo, "Version"),
            // The game's version overlay renders this timestamp in UTC, and a
            // manifest records what the overlay showed. Comparing it in local time
            // is how a build stamped late in the day looks like the day before.
            BuildDateUtc: ReleaseInfoDateUtc(releaseInfo),
            Commit: ReleaseInfoString(releaseInfo, "Commit"),
            Branch: ReleaseInfoString(releaseInfo, "Branch"),
            ContentHash: contentHash,
            PristineAssemblySha256: HashOfLoadedAssembly(assemblyPath),
            Notes: notes);
    }

    private static string ReleaseInfoString(object releaseInfo, string property) =>
        releaseInfo.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(releaseInfo) as string
        ?? throw new EngineException($"The running game's release info has no '{property}'.");

    private static string ReleaseInfoDateUtc(object releaseInfo)
    {
        var value = releaseInfo.GetType().GetProperty("Date", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(releaseInfo);
        if (value is not DateTime date)
        {
            throw new EngineException("The running game's release info has no build date.");
        }

        return date.ToUniversalTime().ToString("yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string HashOfLoadedAssembly(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            throw new EngineException(
                "The running game's assembly has no readable location on disk, so it cannot be identified " +
                "by content.");
        }

        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void VerifyPreparedOutputs(string libDir, JsonObject receipt)
    {
        var hashes = receipt["prepared_output_sha256"]?.AsObject()
            ?? throw new EngineException("The prepared assembly receipt has no output hashes.");
        var assemblies = receipt["assemblies"]?.AsArray()
            ?? throw new EngineException("The prepared assembly receipt has no assembly list.");

        if (hashes["release_info.json"] is null)
        {
            throw new EngineException("The prepared assembly receipt has no release-info hash.");
        }

        foreach (var node in assemblies)
        {
            var name = node?.GetValue<string>()
                ?? throw new EngineException("The prepared assembly receipt contains an invalid assembly name.");
            if (Path.GetFileName(name) != name || hashes[name] is null)
            {
                throw new EngineException($"The prepared assembly receipt has no valid hash for '{name}'.");
            }
        }

        var receiptedDlls = hashes
            .Where(entry => entry.Key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unreceiptedDlls = Directory.GetFiles(libDir, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !receiptedDlls.Contains(name))
            .ToList();
        if (unreceiptedDlls.Count > 0)
        {
            throw new EngineException(
                $"Prepared assembly directory contains unreceipted DLLs: " +
                $"{string.Join(", ", unreceiptedDlls)}. Re-run ./scripts/bootstrap.sh.");
        }

        foreach (var (name, expectedNode) in hashes)
        {
            if (Path.GetFileName(name) != name || expectedNode is null)
            {
                throw new EngineException("The prepared assembly receipt contains an invalid output hash entry.");
            }

            var path = Path.Combine(libDir, name);
            if (!File.Exists(path))
            {
                throw new EngineException($"Prepared output '{name}' is missing. Re-run ./scripts/bootstrap.sh.");
            }

            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
            var expected = expectedNode.GetValue<string>();
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new EngineException(
                    $"Prepared output '{name}' does not match its bootstrap receipt. " +
                    "The private assembly copy changed; re-run ./scripts/bootstrap.sh.");
            }
        }
    }
}

/// <summary>
/// Gives the engine its own release information.
///
/// The engine looks for <c>release_info.json</c> relative to the process working
/// directory, which is wherever the caller happened to be. Rather than depend on
/// that, the file is read from beside the prepared assembly and handed to the
/// engine directly, so the engine reports the build it is actually running.
/// </summary>
internal static class ReleaseInfoBinding
{
    internal static void Install(List<string> failures)
    {
        try
        {
            var libDir = AssemblyResolution.ResolveLibDirectory();
            var path = libDir is null ? null : Path.Combine(libDir, "release_info.json");
            if (path is null || !File.Exists(path))
            {
                failures.Add("release info: not found beside the prepared assembly");
                return;
            }

            var assembly = typeof(MegaCrit.Sts2.Core.Models.ModelDb).Assembly;
            var releaseInfoType = assembly.GetType("MegaCrit.Sts2.Core.Debug.ReleaseInfo");
            var managerType = assembly.GetType("MegaCrit.Sts2.Core.Debug.ReleaseInfoManager");
            if (releaseInfoType is null || managerType is null)
            {
                failures.Add("release info: ReleaseInfo/ReleaseInfoManager absent from this build");
                return;
            }

            var releaseInfo = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                releaseInfoType,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            if (releaseInfo is null)
            {
                failures.Add("release info: file present but did not deserialize");
                return;
            }

            var instance = managerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (instance is null)
            {
                failures.Add("release info: ReleaseInfoManager.Instance was null");
                return;
            }

            var field = managerType.GetField(
                "<ReleaseInfo>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is null)
            {
                failures.Add("release info: ReleaseInfo backing field is absent");
                return;
            }
            field.SetValue(instance, releaseInfo);
        }
        catch (Exception ex)
        {
            failures.Add($"release info: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
