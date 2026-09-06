using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Every game member something had patched at run start, with who patched it.
///
/// This is the one reading available inside a player's process that a mod's own
/// declaration cannot lie to. <see cref="ModEnvironment"/> records what each loaded
/// mod *says* about itself - a manifest, an id, a version, an
/// <c>affects_gameplay</c> flag - and the content hash beside it is blind to
/// behaviour patches. A mod that declares itself non-gameplay and prefixes the
/// combat state or a random stream declares nothing and appears here by member name.
///
/// It is a reading rather than an assessment, like the mod list. What it names is
/// what Harmony reported: a patch that resolved and attached. A patch that silently
/// failed to attach - the failure mode a build's rename produces - is one this list
/// does not contain, which is exactly why the recorder's own patches being absent
/// from it is a refusal rather than an empty answer. See
/// <c>EnvironmentPreflight</c>, which is where that judgement is made.
///
/// Absent from a manifest a video was reconstructed from: nobody watching footage
/// could take this reading, and a guess would be worse than an absence.
/// </summary>
public sealed record PatchRoster
{
    /// <summary>
    /// The patched members, ordered so that two readings of the same process produce
    /// the same list. A roster whose order came from a hash table would differ from
    /// itself between runs and could not be compared with anything.
    /// </summary>
    [JsonPropertyName("members")]
    public required IReadOnlyList<PatchedMember> Members { get; init; }

    /// <summary>
    /// The Harmony id Runmobile patches under, held here because it is the one thing
    /// that tells our patches from somebody else's in a roster.
    ///
    /// One owner for the string: <c>RunmobileMod</c> constructs its Harmony instance
    /// from this const and <c>EnvironmentPreflight</c> judges a roster against it, so
    /// the mod cannot rename itself out of its own rule. Held in this project rather
    /// than in the mod for the same reason <c>EnvironmentPreflight</c> holds the mod
    /// id: the rules have no dependency on anything that runs inside the game.
    /// </summary>
    public const string HostOwnerId = "sts2-pilot-trainer.runmobile";

    /// <summary>Every member on this roster that somebody other than Runmobile
    /// patched.</summary>
    [JsonIgnore]
    public IReadOnlyList<PatchedMember> PatchedByAnybodyElse =>
        Members.Where(member => member.ForeignOwners.Count > 0).ToList();

    /// <summary>
    /// Whether Runmobile's own patches appear on the roster it took.
    ///
    /// False is not "no mods were loaded": the shell installs the profile write
    /// barrier and its own screen patches before it reports itself started, and the
    /// recorder only records inside a started shell. So a roster with no member of
    /// ours in it is a reading that did not see the patches this process definitely
    /// applied, and nothing it says about anybody else's can be trusted either.
    /// </summary>
    [JsonIgnore]
    public bool NamesTheHost => Members.Any(member => member.Owners.Contains(HostOwnerId));
}

/// <summary>
/// One patched member: where it lives, what it is called, who patched it and with
/// how many patches of each kind.
///
/// The counts are here because they are what a fingerprint is for. Two readings of
/// the same build agree on them; one taken after a game update that moved a member
/// does not, and the difference is legible without decompiling anything.
/// </summary>
public sealed record PatchedMember(
    [property: JsonPropertyName("declaring_type")] string DeclaringType,
    [property: JsonPropertyName("member")] string Member,
    [property: JsonPropertyName("owners")] IReadOnlyList<string> Owners,
    [property: JsonPropertyName("prefixes")] int Prefixes,
    [property: JsonPropertyName("postfixes")] int Postfixes,
    [property: JsonPropertyName("transpilers")] int Transpilers,
    [property: JsonPropertyName("finalizers")] int Finalizers)
{
    /// <summary>Whoever patched this member that is not Runmobile.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> ForeignOwners =>
        Owners.Where(owner => !string.Equals(owner, PatchRoster.HostOwnerId, StringComparison.Ordinal)).ToList();

    /// <summary>
    /// One line naming this member, its owners and its patch counts.
    ///
    /// The mod writes these into the game's own log at startup, so a <c>godot.log</c>
    /// attached to a bug report already answers "what else was patching this game".
    /// Formatted here rather than in the mod so the sentence has a test on a machine
    /// with no game installed.
    /// </summary>
    public string Describe() =>
        $"{DeclaringType}.{Member} patched by {string.Join(", ", Owners)} " +
        $"({Prefixes} prefix, {Postfixes} postfix, {Transpilers} transpiler, {Finalizers} finalizer)";
}
