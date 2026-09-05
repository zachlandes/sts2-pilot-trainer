using System.Reflection;
using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Every type in the mod assembly can be loaded before this mod's own assembly
/// resolver exists.
///
/// This is the check that was missing when a build produced a mod the client reported
/// as <c>Loaded 0 mods</c>. The game finds a mod's initializer by calling
/// <c>Assembly.GetTypes()</c> on the DLL it has just loaded, and it does that
/// <em>before</em> calling the initializer - which is where
/// <see cref="Sts2PilotTrainer.Mod.SiblingAssemblies"/> teaches the runtime that this
/// mod's other assemblies sit beside it. At the moment the game enumerates them,
/// <c>Sts2PilotTrainer.Replay</c> and <c>Sts2PilotTrainer.Engine</c> cannot be found;
/// <c>GetTypes()</c> throws rather than returning the types it could load, and the mod
/// is reported as failed with nothing of it running.
///
/// Loading a type needs three things resolved: its base type, the interfaces it
/// implements, and the types of its value-type instance fields, which decide its
/// layout. Method bodies, method signatures, static fields and reference-typed fields
/// are all resolved lazily, by which time the resolver is installed - which is why the
/// mod could always call into the siblings freely and why this rule is narrower than
/// "do not mention them".
///
/// Checked as the rule rather than by loading, because the process running this test
/// has the sibling assemblies loaded already and would resolve them however isolated
/// the context claimed to be. The rule is exactly what the runtime asks, and a reader
/// can see which of the three a new type broke.
/// </summary>
public sealed class ModAssemblyLoadabilityTests
{
    /// <summary>This mod's own assemblies, which the game cannot find until the mod
    /// initializer has run.</summary>
    private static readonly string[] Siblings =
    [
        "Sts2PilotTrainer.Replay",
        "Sts2PilotTrainer.Engine",
        "Sts2PilotTrainer.Trainer",
        "Sts2PilotTrainer.IO",
    ];

    [GameFact]
    public void NoTypeNeedsASiblingAssemblyBeforeTheResolverIsInstalled()
    {
        // The game assembly is one this mod may name in a type definition, because the
        // game is what loaded it. Forced into this process first, or enumerating the
        // mod's types fails here for a reason that is not the one being tested.
        _ = EngineHost.StartupPhase();

        var problems = typeof(Sts2PilotTrainer.Mod.RunmobileMod).Assembly.GetTypes()
            .SelectMany(Problems)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            problems.Count == 0,
            "The game enumerates a mod's types before calling its initializer, so a type that needs one of " +
            "this mod's sibling assemblies in order to load makes the whole mod fail with \"Loaded 0 mods\". " +
            "Reach the siblings through a method instead.\n  " + string.Join("\n  ", problems));
    }

    private static IEnumerable<string> Problems(Type type)
    {
        if (type.BaseType is { } baseType && FromASibling(baseType))
        {
            yield return $"{Name(type)} inherits from {Name(baseType)}.";
        }

        foreach (var contract in type.GetInterfaces().Where(FromASibling))
        {
            yield return $"{Name(type)} implements {Name(contract)}.";
        }

        foreach (var field in type
                     .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                BindingFlags.DeclaredOnly)
                     .Where(field => field.FieldType.IsValueType))
        {
            foreach (var part in Parts(field.FieldType).Where(FromASibling))
            {
                yield return
                    $"{Name(type)} holds a value of {Name(part)} in its field '{field.Name}', which decides " +
                    "its layout.";
            }
        }
    }

    /// <summary>A value type and every value type inside it, since a layout is decided
    /// by all of them.</summary>
    private static IEnumerable<Type> Parts(Type type) =>
        type.IsGenericType
            ? [type.GetGenericTypeDefinition(), .. type.GetGenericArguments().SelectMany(Parts)]
            : [type];

    private static bool FromASibling(Type type) =>
        Siblings.Contains(type.Assembly.GetName().Name, StringComparer.Ordinal);

    private static string Name(Type type) => type.FullName ?? type.Name;
}
