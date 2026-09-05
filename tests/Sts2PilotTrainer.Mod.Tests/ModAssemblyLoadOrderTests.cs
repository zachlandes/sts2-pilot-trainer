using System.Reflection;
using System.Runtime.Loader;
using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The one thing the game does to this assembly before it runs a line of our code,
/// and the one failure no other test here can see.
///
/// MegaCrit's `ModManager.TryLoadMod` calls `Module.GetTypes()` on the mod assembly
/// before it calls the mod's initializer. Loading a type resolves its base class, the
/// interfaces it implements and enough of its instance fields to lay it out, so any of
/// those reaching into a sibling assembly makes the runtime resolve that sibling right
/// there - one phase before `SiblingAssemblies` has taught it where the siblings live.
/// The whole mod then fails to load with a `ReflectionTypeLoadException`, and the game
/// reports "Loaded 0 mods".
///
/// It has happened four times, in four shapes, and each time the rule written after it
/// was narrower than the next one. `IReadOnlyList&lt;MenuRow&gt;` as a field was the
/// first and cost a startup to learn; a `PlaybackSpeed` field was the second, which is
/// why `_speedIndex` is an int; a `(Control, Func&lt;ElementSurface&gt;)?` field was
/// the third, which reached a green pull request and a passing CI run before a retail
/// launch found it; and a type <em>implementing</em> `IFightSampleSink` was the fourth,
/// which no rule about fields covered at all.
///
/// So this test is the arbiter and the prose is not. It reproduces the condition rather
/// than any description of it: enumerate every type in the built mod with the siblings
/// deliberately unreachable, and fail with the offending type named. A new type is
/// checked by running this, never by deciding whether it resembles one of the four.
/// Every other suite here misses the whole class of failure, because they load this
/// assembly where its siblings are already resolvable - which is exactly the condition
/// the game does not provide.
///
/// So this test reproduces the condition rather than the symptom: enumerate every type
/// in the built mod with the siblings deliberately unreachable, and fail with the
/// offending type named. A comment warning about the trap sat forty lines from the
/// field that last hit it; a test does not have to be read to work.
/// </summary>
public sealed class ModAssemblyLoadOrderTests
{
    private static string ModAssemblyPath => Path.Combine(AppContext.BaseDirectory, "Runmobile.dll");

    /// <summary>The assemblies the game cannot resolve at this moment. They ship
    /// beside the mod and are found later, by the mod's own resolver.</summary>
    private static readonly string[] Siblings =
    [
        "Sts2PilotTrainer.Trainer",
        "Sts2PilotTrainer.Engine",
        "Sts2PilotTrainer.Replay",
        "Sts2PilotTrainer.IO",
    ];

    [LoadOrderFact]
    public void EveryTypeIsEnumerableBeforeTheSiblingsCanBeResolved()
    {
        // The game's own assembly has to be resolvable, because in the game it is
        // already loaded - this is about the siblings and nothing else. It is a
        // reference this project deliberately does not copy, so it reaches the default
        // context only once something has started the host; left to whatever ran first,
        // this test reported sts2 itself as an unreachable sibling.
        _ = EngineHost.StartupPhase();

        var context = new WithoutSiblings();
        var mod = context.LoadFromAssemblyPath(ModAssemblyPath);

        try
        {
            foreach (var module in mod.Modules) module.GetTypes();
        }
        catch (ReflectionTypeLoadException failure)
        {
            // Name the type rather than only the assembly: the game's own message says
            // which sibling it wanted and never which of our types wanted it, which is
            // what made this expensive to find each time.
            var blamed = failure.Types
                .Where(type => type is not null)
                .Select(type => type!.FullName)
                .ToArray();
            var reasons = failure.LoaderExceptions
                .Select(reason => reason?.Message)
                .Where(message => message is not null)
                .Distinct()
                .ToArray();

            Assert.Fail(
                "The game enumerates this assembly's types before the mod's own assembly resolver exists, " +
                "so nothing a type needs in order to load may reach into a sibling assembly: not its base " +
                "class, not an interface it implements, and not the type of an instance field - the field's " +
                "own type, a generic argument, or a tuple element inside it. Method bodies, method " +
                "signatures, static fields and reference-typed fields are resolved later and are fine. " +
                "Hold such state as a plain reference or as an int and read the real thing back on use, the " +
                "way _speedIndex and _phase already do; reach an interface through a delegating " +
                "implementation that lives in the sibling, the way DelegatingFightSampleSink is reached.\n" +
                $"Loader said: {string.Join("; ", reasons)}\n" +
                $"Types that did load: {blamed.Length} of {failure.Types.Length}.");
        }
    }

    /// <summary>
    /// A load context that can find the mod and nothing of ours beside it.
    ///
    /// Returning null from Load falls back to the default context, which in a test run
    /// would find the siblings sitting in the same output directory and prove nothing.
    /// Throwing is what makes this the game's situation rather than ours. Everything
    /// else still comes from the default context, and the game's assembly on purpose:
    /// loading a second copy of it here would leave two in the process, which is not
    /// the game's situation either.
    /// </summary>
    private sealed class WithoutSiblings() : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name) =>
            Siblings.Contains(name.Name)
                ? throw new FileNotFoundException(
                    $"Could not load file or assembly '{name.Name}'. The system cannot find the file specified.")
                : null;
    }

    public sealed class LoadOrderFactAttribute : FactAttribute
    {
        public LoadOrderFactAttribute()
        {
            if (!File.Exists(ModAssemblyPath))
            {
                Skip = "Needs the built Runmobile mod. Run ./scripts/build.sh.";
            }
        }
    }
}
