using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Where a value in a manifest came from. Recorded on every value that could
/// plausibly have come from somewhere else, because the whole integrity claim of
/// this project rests on never mistaking a reconstruction for an observation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FactSource>))]
public enum FactSource
{
    /// <summary>Read directly off the source video. The strongest kind of claim
    /// this project makes about the outside world, and the only one a reader can
    /// re-check independently from the video ID and timestamp.</summary>
    Observed,

    /// <summary>Derived by reasoning from observations plus documented game rules.
    /// Never seen. An inference that turns out wrong is a manifest defect, and the
    /// arbiter is what catches it.</summary>
    Inferred,

    /// <summary>Produced by the game engine during replay. Not evidence about the
    /// video - it is what the engine says follows from the actions. Comparing an
    /// <see cref="Engine"/> value against an <see cref="Observed"/> one is the
    /// entire point of a checkpoint.</summary>
    Engine,

    /// <summary>Fixed by the manifest author as an input constant, such as an
    /// identifier this project invented. Carries no claim about the game.</summary>
    Declared,
}

/// <summary>
/// A value together with how it was established. <paramref name="Evidence"/> is
/// for a human re-checking the claim; it never affects replay, hashing, or cache
/// identity, so re-annotating a manifest cannot invalidate a verified snapshot.
/// </summary>
public sealed record Fact<T>(
    T Value,
    FactSource Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FactEvidence? Evidence = null)
{
    public static Fact<T> Observed(T value, FactEvidence evidence) => new(value, FactSource.Observed, evidence);

    public static Fact<T> Inferred(T value, FactEvidence evidence) => new(value, FactSource.Inferred, evidence);

    public static Fact<T> Engine(T value) => new(value, FactSource.Engine);

    public static Fact<T> Declared(T value) => new(value, FactSource.Declared);

    public override string ToString() => $"{Value} ({Source.ToString().ToLowerInvariant()})";
}

/// <summary>
/// How to re-check a fact against its source. For a video observation that means
/// the timestamp and the reading method, which together let anyone open the public
/// video and look. No footage is stored, only the coordinates of the evidence.
/// </summary>
public sealed record FactEvidence(
    [property: JsonPropertyName("video_t_ms")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? VideoTimeMs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Method = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Note = null)
{
    public static FactEvidence AtVideoTime(int videoTimeMs, string method, string? note = null) =>
        new(videoTimeMs, method, note);

    public static FactEvidence Reasoning(string note) => new(Note: note);
}
