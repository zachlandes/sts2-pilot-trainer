using System.Reflection;
using System.Runtime.Loader;
using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed class ModeCardTests
{
    private static string ModAssemblyPath =>
        Path.Combine(
            Arbiter.RepoRoot, "build", "bin", "Sts2PilotTrainer.Mod", "Release", "net9.0", "CombatTrainer.dll");

    [ModeCardFact]
    public void TranslationChangeKeepsTheApprovedTitleAndDescription()
    {
        _ = EngineHost.StartupPhase();
        var modAssembly = AssemblyLoadContext.Default.Assemblies
            .FirstOrDefault(assembly => assembly.GetName().Name == "CombatTrainer")
            ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(ModAssemblyPath);
        var gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .Single(assembly => assembly.GetName().Name == "sts2");
        var cardType = gameAssembly.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSubmenuButton")!;
        var titleType = gameAssembly.GetType("MegaCrit.Sts2.addons.mega_text.MegaLabel")!;
        var descriptionType = gameAssembly.GetType("MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel")!;
        var card = Activator.CreateInstance(cardType)!;
        var title = Activator.CreateInstance(titleType)!;
        var description = Activator.CreateInstance(descriptionType)!;
        SetField(cardType, card, "_title", title);
        SetField(cardType, card, "_description", description);
        SetField(cardType, card, "_locKeyPrefix", "CUSTOM");

        var modeCard = modAssembly.GetType("Sts2PilotTrainer.Mod.ModeCard")!;
        modeCard.GetMethod("SetLabels", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [card]);
        cardType.GetMethod("_Notification")!.Invoke(card, [2010]);

        Assert.Null(GetField(cardType, card, "_locKeyPrefix"));
        Assert.Equal("Combat Trainer", TextOf(title));
        Assert.Equal(
            "Fight NaveGreed's Floor 2 Sludge Spinner exactly as recorded, then compare your fight with " +
            "the recording. Reads your game; never writes to it.",
            TextOf(description));
    }

    private static object? GetField(Type type, object target, string name) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static void SetField(Type type, object target, string name, object? value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static string? TextOf(object label) =>
        label.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public)!.GetValue(label) as string;

    public sealed class ModeCardFactAttribute : FactAttribute
    {
        public ModeCardFactAttribute()
        {
            if (!Arbiter.GameAvailable || !File.Exists(ModAssemblyPath))
            {
                Skip = "Needs the prepared game and built Combat Trainer mod. Run ./scripts/build.sh.";
            }
        }
    }
}
