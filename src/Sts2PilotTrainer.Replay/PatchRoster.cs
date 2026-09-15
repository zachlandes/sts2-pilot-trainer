using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Every game member something had patched at run start, with who patched it, and
/// the same reading at run end where the recorder was new enough to take it.
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
    /// <summary>The manifest format whose recorder first read the roster again at run end.</summary>
    public const int RunEndIntroducedInManifestVersion = 8;

    /// <summary>
    /// The patched members, ordered so that two readings of the same process produce
    /// the same list. A roster whose order came from a hash table would differ from
    /// itself between runs and could not be compared with anything.
    /// </summary>
    [JsonPropertyName("members")]
    public required IReadOnlyList<PatchedMember> Members { get; init; }

    /// <summary>
    /// The same registry read when the run ended.
    ///
    /// Nested under the start roster rather than beside it as a second environment
    /// record: the two readings answer one question, whether the patch environment
    /// stayed the same for the run. The fact carries the last action ordinal and run
    /// clock so the second reading does not inherit the start reading's coordinates.
    /// Null only for a manifest reconstructed from a video or migrated from a format
    /// whose recorder never took the second reading.
    /// </summary>
    [JsonPropertyName("run_end")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Fact<PatchRoster>? AtRunEnd { get; init; }

    /// <summary>
    /// Whether both readings name the same members, owners and patch counts.
    ///
    /// Null means there was no end reading, never that an absent reading matched.
    /// Member and owner order are normalised because registry order is not part of
    /// the environment; every value Harmony reported is.
    /// </summary>
    [JsonIgnore]
    public bool? StayedTheSame => AtRunEnd is null ? null : SameMembers(Members, AtRunEnd.Value.Members);

    /// <summary>The members whose presence, owners or patch counts differ between
    /// the two readings, by their stable type-and-signature identity.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> ChangedMembers => AtRunEnd is null
        ? []
        : Members.Select(Identity)
            .Concat(AtRunEnd.Value.Members.Select(Identity))
            .Distinct(StringComparer.Ordinal)
            .Where(identity => !SameMembers(
                Members.Where(member => Identity(member) == identity),
                AtRunEnd.Value.Members.Where(member => Identity(member) == identity)))
            .OrderBy(identity => identity, StringComparer.Ordinal)
            .ToList();

    private static bool SameMembers(IEnumerable<PatchedMember> left, IEnumerable<PatchedMember> right) =>
        left.Select(Fingerprint).OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(right.Select(Fingerprint).OrderBy(value => value, StringComparer.Ordinal),
                StringComparer.Ordinal);

    private static string Identity(PatchedMember member) => $"{member.DeclaringType}.{member.Member}";

    private static string Fingerprint(PatchedMember member) =>
        $"{Identity(member)}\n{string.Join("\n", member.Owners.OrderBy(owner => owner, StringComparer.Ordinal))}\n" +
        $"{member.Prefixes}\n{member.Postfixes}\n{member.Transpilers}\n{member.Finalizers}";

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
