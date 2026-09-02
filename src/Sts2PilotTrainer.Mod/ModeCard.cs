using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Puts a fourth card in the singleplayer menu, beside Standard, Daily and Custom.
///
/// The card is a duplicate of the game's own Custom Run card rather than a control
/// built from parts. That is what makes it native: it keeps the scene's panel,
/// shader, hover tween, focus behaviour, hotkey icon and controller navigation,
/// because they are the same nodes the game authored. Nothing is drawn by this mod;
/// only the two labels are replaced and the released signal is pointed somewhere
/// else.
///
/// This mod ships no resource pack, so the card also keeps the icon it was
/// duplicated from. Art of its own needs a <c>.pck</c>, which the packaging contract
/// in docs/distribution.md deliberately does without.
/// </summary>
[HarmonyPatch(typeof(NSingleplayerSubmenu))]
internal static class ModeCard
{
    /// <summary>The node name the game gives the card this one is duplicated from.
    /// Its own <c>_Ready</c> resolves it by this path, so a build that renamed it
    /// would have stopped working before reaching us.</summary>
    private const string SourceCardPath = "CustomRunButton";

    private const string StandardCardPath = "StandardButton";
    private const string DailyCardPath = "DailyButton";

    /// <summary>The name given to the card this mod adds, so a second pass can see
    /// its own work rather than adding another one.</summary>
    private const string CardNodeName = "CombatTrainerButton";

    /// <summary>
    /// Runs after the game has built the singleplayer menu.
    ///
    /// A postfix rather than a replacement: the game's own three cards are wired by
    /// the method this follows, and anything that skipped it would be a mod that
    /// reimplements the menu.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NSingleplayerSubmenu._Ready))]
    internal static void AddCard(NSingleplayerSubmenu __instance)
    {
        try
        {
            if (__instance.GetNodeOrNull<NSubmenuButton>(CardNodeName) is not null) return;

            // The first moment there is demonstrably a running game to read. Mod
            // loading is not: it runs before the game has a model database at all.
            if (!CombatTrainerMod.EnsureAdopted()) return;

            var source = __instance.GetNodeOrNull<NSubmenuButton>(SourceCardPath);
            if (source is null)
            {
                Log.Warn(
                    $"[{CombatTrainerMod.ModId}] this build's singleplayer menu has no '{SourceCardPath}' " +
                    "card to model a fourth one on; not adding one.", 2);
                return;
            }

            const int duplicateFlags =
                (int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts | Node.DuplicateFlags.UseInstantiation);
            if (source.Duplicate(duplicateFlags) is not NSubmenuButton card)
            {
                Log.Warn(
                    $"[{CombatTrainerMod.ModId}] the singleplayer card could not be duplicated; not adding " +
                    "a fourth one.", 2);
                return;
            }

            InstallCard(
                __instance,
                source,
                card,
                () =>
                {
                    var error = card.Connect(
                        NClickableControl.SignalName.Released,
                        Callable.From<NButton>(_ => TrainerScreen.Open()));
                    if (error != Error.Ok)
                    {
                        throw new InvalidOperationException($"Connecting the mode card failed with {error}.");
                    }
                });
        }
        catch (Exception ex)
        {
            // The player's main menu is not ours to break. A card that failed to
            // appear is a bug report; a menu that failed to open is a broken game.
            Log.Error(
                $"[{CombatTrainerMod.ModId}] could not add the mode card: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    private static void InstallCard(
        NSingleplayerSubmenu submenu,
        NSubmenuButton source,
        NSubmenuButton card,
        Action connect)
    {
        var layout = Layout.Capture(submenu, source);
        var added = false;
        try
        {
            card.Name = CardNodeName;
            submenu.AddChild(card);
            added = true;
            SetLabels(card);
            Layout.PlaceBeside(layout, card);
            connect();
        }
        catch
        {
            try
            {
                Layout.Restore(layout);
            }
            finally
            {
                try
                {
                    if (added && card.GetParent() == submenu) submenu.RemoveChild(card);
                }
                finally
                {
                    card.QueueFree();
                }
            }
            throw;
        }
    }

    /// <summary>
    /// Replaces the duplicated card's two labels with this mod's own wording.
    ///
    /// Set on the label nodes rather than through <c>SetIconAndLocalization</c>,
    /// which reads the game's own localization tables. A DLL-only mod contributes no
    /// tables, so asking for a key that does not exist would put a key on screen.
    /// The duplicate's localization prefix is cleared so a translation refresh
    /// leaves these labels unchanged.
    /// </summary>
    private static void SetLabels(NSubmenuButton card)
    {
        ClearLocalization(card);
        var title = Field<MegaLabel>(card, "_title");
        var description = Field<MegaRichTextLabel>(card, "_description");
        title.SetTextAutoSize(TrainerCopy.Name);
        description.SetTextAutoSize(TrainerCopy.Description);
    }

    private static T Field<T>(NSubmenuButton card, string name) where T : class
    {
        var field = CardField(name);
        return field.GetValue(card) as T
            ?? throw new InvalidOperationException(
                $"NSubmenuButton.{name} was not a {typeof(T).Name} on this build.");
    }

    private static void ClearLocalization(NSubmenuButton card) =>
        CardField("_locKeyPrefix").SetValue(card, null);

    private static FieldInfo CardField(string name) =>
        typeof(NSubmenuButton).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            $"NSubmenuButton has no '{name}' on this build, so the card's wording cannot be set.");

    /// <summary>
    /// Where the fourth card goes.
    ///
    /// When the game lays the cards out in a container, the container decides and
    /// this does nothing. When it positions them itself - which is what v0.111.0
    /// does - the step between two of the game's own cards is measured and reused,
    /// and the whole row is shifted back by half a step so four cards stay centred
    /// where three were. Measured rather than written down, so a build that changes
    /// the spacing changes this with it.
    /// </summary>
    private static class Layout
    {
        internal sealed record Snapshot(
            Control Source,
            Control? Standard,
            Control? Daily,
            IReadOnlyList<(Control Card, Vector2 Position)> Positions);

        internal static Snapshot Capture(NSingleplayerSubmenu submenu, Control source)
        {
            var standard = submenu.GetNodeOrNull<Control>(StandardCardPath);
            var daily = submenu.GetNodeOrNull<Control>(DailyCardPath);
            var positions = new[] { standard, daily, source }
                .OfType<Control>()
                .Distinct()
                .Select(card => (card, card.Position))
                .ToList();
            return new Snapshot(source, standard, daily, positions);
        }

        internal static void PlaceBeside(Snapshot snapshot, Control card)
        {
            if (card.GetParent() is Container || snapshot.Standard is null || snapshot.Daily is null) return;

            var step = snapshot.Daily.Position - snapshot.Standard.Position;
            if (step.LengthSquared() <= 0f) return;

            card.Position = snapshot.Source.Position + step;
            var recentre = -step / 2f;
            foreach (var sibling in new[] { snapshot.Standard, snapshot.Daily, snapshot.Source, card })
            {
                sibling.Position += recentre;
            }
        }

        internal static void Restore(Snapshot snapshot)
        {
            foreach (var (card, position) in snapshot.Positions) card.Position = position;
        }
    }
}
