using System.Reflection;
using System.Runtime.Loader;
using Godot;
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

    [ModeCardFact]
    public void FailedInstallationRemovesTheCardAndRestoresTheNativeLayout()
    {
        _ = EngineHost.StartupPhase();
        var modAssembly = AssemblyLoadContext.Default.Assemblies
            .FirstOrDefault(assembly => assembly.GetName().Name == "CombatTrainer")
            ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(ModAssemblyPath);
        var gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .Single(assembly => assembly.GetName().Name == "sts2");
        var submenuType = gameAssembly.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSingleplayerSubmenu")!;
        var cardType = gameAssembly.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSubmenuButton")!;
        var titleType = gameAssembly.GetType("MegaCrit.Sts2.addons.mega_text.MegaLabel")!;
        var descriptionType = gameAssembly.GetType("MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel")!;
        var submenu = (Node)Activator.CreateInstance(submenuType)!;
        var standard = CreateCard(cardType, titleType, descriptionType, "StandardButton", new Vector2(10, 20));
        var daily = CreateCard(cardType, titleType, descriptionType, "DailyButton", new Vector2(110, 20));
        var source = CreateCard(cardType, titleType, descriptionType, "CustomRunButton", new Vector2(210, 20));
        var trainer = CreateCard(cardType, titleType, descriptionType, "Duplicate", new Vector2(210, 20));
        submenu.AddChild(standard);
        submenu.AddChild(daily);
        submenu.AddChild(source);
        var originalPositions = new[] { standard.Position, daily.Position, source.Position };

        var modeCard = modAssembly.GetType("Sts2PilotTrainer.Mod.ModeCard")!;
        var failure = Assert.Throws<TargetInvocationException>(() =>
            modeCard.GetMethod("InstallCard", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [submenu, source, trainer, (Action)(() => throw new InvalidOperationException())]));

        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Null(trainer.GetParent());
        Assert.Null(submenu.GetNodeOrNull<Node>("CombatTrainerButton"));
        Assert.Equal(originalPositions, new[] { standard.Position, daily.Position, source.Position });
    }

    [ModeCardFact]
    public void UnsupportedManualLayoutsRemoveTheCardAndRestoreNativePositions()
    {
        AssertUnsupportedLayoutRollsBack(includeStandard: false, dailyPosition: new Vector2(110, 20));
        AssertUnsupportedLayoutRollsBack(includeStandard: true, dailyPosition: new Vector2(10, 20));
    }

    private static void AssertUnsupportedLayoutRollsBack(bool includeStandard, Vector2 dailyPosition)
    {
        _ = EngineHost.StartupPhase();
        var modAssembly = AssemblyLoadContext.Default.Assemblies
            .FirstOrDefault(assembly => assembly.GetName().Name == "CombatTrainer")
            ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(ModAssemblyPath);
        var gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .Single(assembly => assembly.GetName().Name == "sts2");
        var submenuType = gameAssembly.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSingleplayerSubmenu")!;
        var cardType = gameAssembly.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSubmenuButton")!;
        var titleType = gameAssembly.GetType("MegaCrit.Sts2.addons.mega_text.MegaLabel")!;
        var descriptionType = gameAssembly.GetType("MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel")!;
        var submenu = (Node)Activator.CreateInstance(submenuType)!;
        var standard = CreateCard(cardType, titleType, descriptionType, "StandardButton", new Vector2(10, 20));
        var daily = CreateCard(cardType, titleType, descriptionType, "DailyButton", dailyPosition);
        var source = CreateCard(cardType, titleType, descriptionType, "CustomRunButton", new Vector2(210, 20));
        var trainer = CreateCard(cardType, titleType, descriptionType, "Duplicate", new Vector2(210, 20));
        if (includeStandard) submenu.AddChild(standard);
        submenu.AddChild(daily);
        submenu.AddChild(source);
        var originalPositions = new[] { standard.Position, daily.Position, source.Position };

        var modeCard = modAssembly.GetType("Sts2PilotTrainer.Mod.ModeCard")!;
        var failure = Assert.Throws<TargetInvocationException>(() =>
            modeCard.GetMethod("InstallCard", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [submenu, source, trainer, (Action)(() => { })]));

        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Null(trainer.GetParent());
        Assert.Null(submenu.GetNodeOrNull<Node>("CombatTrainerButton"));
        Assert.Equal(originalPositions, new[] { standard.Position, daily.Position, source.Position });
    }

    private static Control CreateCard(
        Type cardType,
        Type titleType,
        Type descriptionType,
        string name,
        Vector2 position)
    {
        var card = (Control)Activator.CreateInstance(cardType)!;
        card.Name = name;
        card.Position = position;
        SetField(cardType, card, "_title", Activator.CreateInstance(titleType));
        SetField(cardType, card, "_description", Activator.CreateInstance(descriptionType));
        SetField(cardType, card, "_locKeyPrefix", "CUSTOM");
        return card;
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
