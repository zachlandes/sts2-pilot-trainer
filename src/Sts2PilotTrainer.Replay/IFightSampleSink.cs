namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Where the samples taken either side of a fight's actions go.
///
/// <see cref="FightCapture"/> is the one that keeps a fight, and the Combat Trainer's
/// observer has fed it directly since there was only one thing watching a fight. The
/// recorder watches the same actions for a different reason - it is keeping a whole
/// run, of which this fight is a part - so the observer needs to be able to hand its
/// samples to either without either one knowing about the other.
///
/// It is deliberately the same five calls <see cref="FightCapture"/> already had,
/// and no more: what a sample means is that class's, and an implementation that
/// decided something different about a gap or an unfinished fight would be the second
/// capture path <c>AGENTS.md</c> forbids. <see cref="RunCapture"/> reaches the same
/// rules by holding one of these per fight rather than by restating them.
/// </summary>
public interface IFightSampleSink
{
    /// <summary>An action is about to happen, with the state it happens from.</summary>
    void BeginStep(
        string verb,
        IReadOnlyDictionary<string, string> args,
        IReadOnlyDictionary<string, string> before,
        bool previousActionFinished = false);

    /// <summary>The state the open action left.</summary>
    void CompleteStep(IReadOnlyDictionary<string, string> after);

    /// <summary>Forget the open action: the game took it back before it took effect.</summary>
    void DiscardOpenStep();

    /// <summary>The fight has ended, with the state it ended in.</summary>
    void Finish(IReadOnlyDictionary<string, string> final);

    /// <summary>The watcher could not account for the fight continuously.</summary>
    void MarkIncomplete(string reason);
}

/// <summary>
/// A sink assembled from delegates, for a host that cannot implement the interface
/// itself.
///
/// The mod's entry assembly is one such host, and not by preference. The game finds a
/// mod's initializer by enumerating that assembly's types <em>before</em> the mod has
/// taught the runtime where its other assemblies are, so a type in there that
/// implements an interface from this one fails to load and takes the whole mod down
/// with it. Reaching this through a method instead is what keeps that from happening;
/// <c>ModAssemblyLoadabilityTests</c> is what keeps it from happening again.
///
/// It decides nothing. Every call is passed straight on, and every rule about what a
/// sample means stays where it was.
/// </summary>
public sealed class DelegatingFightSampleSink(
    Action<string, IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>, bool> beginStep,
    Action<IReadOnlyDictionary<string, string>> completeStep,
    Action discardOpenStep,
    Action<IReadOnlyDictionary<string, string>> finish,
    Action<string> markIncomplete) : IFightSampleSink
{
    public void BeginStep(
        string verb,
        IReadOnlyDictionary<string, string> args,
        IReadOnlyDictionary<string, string> before,
        bool previousActionFinished = false) =>
        beginStep(verb, args, before, previousActionFinished);

    public void CompleteStep(IReadOnlyDictionary<string, string> after) => completeStep(after);

    public void DiscardOpenStep() => discardOpenStep();

    public void Finish(IReadOnlyDictionary<string, string> final) => finish(final);

    public void MarkIncomplete(string reason) => markIncomplete(reason);
}
