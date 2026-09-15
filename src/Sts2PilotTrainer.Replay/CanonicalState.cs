using System.Security.Cryptography;
using System.Text;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// The engine's state, reduced to exactly the fields that are part of the run's
/// identity, in a form two processes can compare byte for byte.
///
/// Built by an explicit allowlist projection rather than by serialising everything
/// and filtering afterwards. That direction matters: an allowlist cannot quietly
/// admit a volatile field the day the engine gains one, and it forces the decision
/// about every field to be made once, in the open, rather than in response to a
/// mismatch. <see cref="ExcludedByDesign"/> records the decisions that were made
/// against inclusion, so "why isn't this here?" has an answer that predates the
/// first time somebody asked.
/// </summary>
public sealed class CanonicalState
{
    private readonly SortedDictionary<string, string> _fields;
    private readonly IReadOnlySet<string> _outsideTheDigest;

    private CanonicalState(SortedDictionary<string, string> fields, IReadOnlySet<string> outsideTheDigest)
    {
        _fields = fields;
        _outsideTheDigest = outsideTheDigest;
    }

    /// <summary>Every projected field, the ones the digest leaves out included.</summary>
    public IReadOnlyDictionary<string, string> Fields => _fields;

    /// <summary>
    /// The fields a trace samples and the digest does not hash.
    ///
    /// A digest identifies a state two hosts have to agree on, and a reading of a
    /// finished fight is not one: the game's own save carries no combat, so a run
    /// continued from a save and the run that was never quit differ in it. A trace is
    /// not compared that way - it is read to say what a fight's steps did - and one
    /// reading of a finished fight is needed there: which side's turn the fight ended
    /// in, without which the step that ended it cannot say whether the enemy it left
    /// behind was killed or fled. The projection names such a field through
    /// <see cref="Builder.AddOutsideTheDigest"/>, and it is this set that keeps the
    /// rendering the digest hashes exactly what the digest means.
    /// </summary>
    public IReadOnlySet<string> OutsideTheDigest => _outsideTheDigest;

    /// <summary>
    /// State that is deliberately never part of the canonical form, and why.
    ///
    /// Every entry here is excluded because it varies between two runs that are, by
    /// the definition this project uses, the same run. Excluding them up front is
    /// what makes a digest mismatch meaningful: it can only be a real divergence,
    /// never an artefact of when or where the replay happened.
    /// </summary>
    public static readonly IReadOnlyList<ExcludedField> ExcludedByDesign =
    [
        new("wall_clock",
            "Any timestamp: run start time, save time, elapsed real time.",
            "Two replays of one manifest happen at different moments. The game's own " +
            "documentation marks its non-gameplay randomness as explicitly not " +
            "reproducible across save and load, so no gameplay value depends on these."),

        new("object_identity",
            "Managed object references, hash codes, and any id derived from allocation " +
            "order, including per-entity net ids.",
            "These are addresses, not state. Two processes that agree perfectly on the " +
            "run will still allocate differently. Entities are identified in the " +
            "canonical form by position and model id instead."),

        new("filesystem_paths",
            "Install directory, prepared-assembly directory, save directory, temp paths.",
            "Machine-specific by construction, and a path in a digest would make a " +
            "verified snapshot un-portable. Also keeps home directories out of any " +
            "artifact this project publishes."),

        new("process_environment",
            "Process id, thread ids, environment variables, culture, locale strings.",
            "Not run state. Localised display text in particular would make the digest " +
            "depend on the language the reader happens to run in."),

        new("presentation",
            "Animation progress, tween state, sound cues, UI focus, camera.",
            "The headless host has no presentation layer at all, so including any of " +
            "this would compare nothing against nothing and look like agreement."),

        new("finished_fight",
            "A fight that has ended: its last turn, energy, empty piles, encounter and " +
            "enemy roster, which the engine keeps on the player until the next fight.",
            "The game's own save carries no combat, so a run continued from a save stands " +
            "at the same place with a fresh combat state or none where the run that was " +
            "never quit carries the fight as it was fought - the same run, and a " +
            "different digest at every shop, rest, event and loot screen after a fight. " +
            "Outside a live fight the canonical form carries only that none is live and, " +
            "off the room the run stands in, how the last one ended."),
    ];

    public static Builder Build() => new();

    /// <summary>
    /// SHA-256 over the canonical rendering. Sorted keys and an explicit, culture-free
    /// encoding, so the digest depends on the state and on nothing else.
    /// </summary>
    public string Digest() => DigestRendering(Render());

    /// <summary>SHA-256 over an already rendered canonical state, for consumers of
    /// the canonical state artifact rather than the in-memory projection.</summary>
    public static string DigestRendering(string canonical)
    {
        var bytes = Encoding.UTF8.GetBytes(canonical);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>Human-readable canonical form. This exact text is what gets hashed,
    /// so a digest mismatch can always be turned into a readable diff.</summary>
    public string Render()
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in _fields)
        {
            if (_outsideTheDigest.Contains(key)) continue;
            sb.Append(key).Append('=').Append(value).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Fields present in one state and not the other, or differing. Ordered,
    /// so two diffs of the same pair read the same way.</summary>
    public static IReadOnlyList<StateDifference> Diff(CanonicalState left, CanonicalState right)
    {
        var keys = new SortedSet<string>(left._fields.Keys, StringComparer.Ordinal);
        keys.UnionWith(right._fields.Keys);

        var differences = new List<StateDifference>();
        foreach (var key in keys)
        {
            left._fields.TryGetValue(key, out var l);
            right._fields.TryGetValue(key, out var r);
            if (!string.Equals(l, r, StringComparison.Ordinal))
            {
                differences.Add(new StateDifference(key, l ?? "<absent>", r ?? "<absent>"));
            }
        }
        return differences;
    }

    /// <summary>
    /// What separates the members of an ordered sequence field.
    ///
    /// Named here because it is read back as well as written: the library's surfaces
    /// read a deck and a relic list out of a recording's own checkpoints, and a second
    /// copy of this character would be a second thing to keep in step with the
    /// projection.
    /// </summary>
    public const char SequenceSeparator = '|';

    /// <summary>
    /// The manifest format that introduced the canonical form this build projects.
    ///
    /// A digest is a hash of the whole projected state, so it means what the
    /// projection meant when it was taken; every boundary digest names this in
    /// <see cref="ReplayBoundary.Projection"/> so a reader can tell one this build can
    /// reproduce from one an older projection produced. Moved with the projection and
    /// never with the manifest format alone: format 7 changed what a finished fight
    /// contributes, so that is where the current form began.
    /// </summary>
    public const int Projection = 7;

    public sealed class Builder
    {
        private readonly SortedDictionary<string, string> _fields = new(StringComparer.Ordinal);
        private readonly HashSet<string> _outsideTheDigest = new(StringComparer.Ordinal);

        /// <summary>Adds a field a trace samples and the digest leaves out; see
        /// <see cref="CanonicalState.OutsideTheDigest"/> for what qualifies.</summary>
        public Builder AddOutsideTheDigest(string field, string value)
        {
            Add(field, value);
            _outsideTheDigest.Add(field);
            return this;
        }

        /// <summary>Adds one allowlisted field. Adding the same field twice is a bug in
        /// the projection, not a last-write-wins convenience, so it throws.</summary>
        public Builder Add(string field, string value)
        {
            if (!_fields.TryAdd(field, value))
            {
                throw new InvalidOperationException(
                    $"Canonical field '{field}' was projected twice. The projection must name each field once.");
            }
            return this;
        }

        public Builder Add(string field, int value) =>
            Add(field, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public Builder Add(string field, bool value) => Add(field, value ? "true" : "false");

        /// <summary>Adds an ordered sequence. Order is preserved because in this game it
        /// is state: hand order and draw order are outcomes of the shuffle stream.</summary>
        public Builder AddSequence(string field, IEnumerable<string> values) =>
            Add(field, string.Join(SequenceSeparator, values));

        public CanonicalState ToState() => new(_fields, _outsideTheDigest);
    }

    public sealed record ExcludedField(string Category, string What, string Why);

    public sealed record StateDifference(string Field, string Left, string Right);
}
