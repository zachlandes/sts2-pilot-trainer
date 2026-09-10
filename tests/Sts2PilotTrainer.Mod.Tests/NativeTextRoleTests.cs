using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What this build actually has, and what happens when it does not have it.
///
/// The first two facts read the game's own scene files, because a role's node path is
/// checked nowhere else until the surface that asks for it is drawn: v0.111.0 shipped
/// with the ledger row pointing at a node that scene does not contain, and the first
/// thing to notice was a player entering a recorded fight and being told the fight was
/// abandoned. The rest hold the mod to naming such a role in the log before the
/// surface that asks for it refuses.
/// </summary>
public sealed class NativeTextRoleTests : IDisposable
{
    /// <summary>
    /// Loading the engine assembly is what teaches this process where the prepared game
    /// assemblies live: its module initializer installs the resolver. Without it the
    /// game's own logger cannot be loaded, and a refused role is written there. Every
    /// other class here reaches a game type on its way in and gets the resolver for
    /// free; this one has no other reason to.
    /// </summary>
    static NativeTextRoleTests() => _ = typeof(EngineHost).Assembly;

    private const int OrdinarySize = 21;

    public void Dispose() => GameText.Forget();

    [NativeSceneFact]
    public void EveryTextRoleNamesANodeThisBuildHas()
    {
        var missing = new List<string>();
        foreach (var (_, scenePath, node, name) in GameText.Declarations)
        {
            var text = NativeScenes.Read(scenePath);
            if (text is null)
            {
                missing.Add($"{name}: this build has no '{scenePath}'");
                continue;
            }

            if (!NativeScenes.Nodes(text).ContainsKey(node))
            {
                missing.Add($"{name}: '{scenePath}' has no '{node}'");
            }
        }

        Assert.Empty(missing);
    }

    [NativeSceneFact]
    public void EveryTextRoleNamesANodeThatCarriesAFontOfItsOwn()
    {
        var plain = new List<string>();
        foreach (var (_, scenePath, node, name) in GameText.Declarations)
        {
            var text = NativeScenes.Read(scenePath);
            if (text is null || !NativeScenes.Nodes(text).TryGetValue(node, out var found)) continue;
            if (!found.CarriesAFont) plain.Add($"{name}: '{node}' declares no font of its own");
        }

        Assert.Empty(plain);
    }

    /// <summary>
    /// The regression, named. The ledger row is the obtained row of the map point's
    /// own hover tip, and the path to it runs through <c>TopContainer</c> - which is
    /// what the shipped mapping left out.
    /// </summary>
    [NativeSceneFact]
    public void TheLedgerRowIsTheObtainedRowUnderTheMapPointTip()
    {
        var (_, scenePath, node, _) = GameText.Declarations
            .Single(declaration => declaration.Role == NativeTextRole.LedgerRow);

        var found = NativeScenes.Nodes(NativeScenes.Read(scenePath)!)[node];

        Assert.Equal("RichTextLabel", found.Type);
        Assert.True(found.CarriesAFont);
    }

    [Fact]
    public void ARoleThisBuildAnswersIsTheStyleItRead()
    {
        GameText.Verify(Reading());

        Assert.Equal(OrdinarySize, GameText.Scene(NativeTextRole.LedgerRow).Size);
    }

    [Fact]
    public void ARolePointingAtANodeThisBuildHasNotIsReportedAtVerification()
    {
        var refused = GameText.Verify(Reading(NativeTextRole.LedgerRow));

        Assert.Equal([NativeTextRole.LedgerRow], refused);
    }

    /// <summary>
    /// A role this build cannot answer refuses where it is used, exactly as it did
    /// before verification existed: this mod never invents a font and a size for
    /// missing native furniture, and naming the role early does not soften that.
    /// </summary>
    [Fact]
    public void ARoleThisBuildCannotAnswerStillRefusesAtUse()
    {
        GameText.Verify(Reading(NativeTextRole.LedgerRow));

        Assert.Throws<InvalidOperationException>(() => GameText.Scene(NativeTextRole.LedgerRow));
    }

    /// <summary>
    /// Stands in for the game's scene files: every role answers, except the ones this
    /// build is being told it has not got.
    /// </summary>
    private static Func<NativeTextRole, GameTextStyle> Reading(params NativeTextRole[] missing) =>
        role => missing.Contains(role)
            ? throw new InvalidOperationException($"This build's '{role}' text role is not here.")
            : new GameTextStyle(null, OrdinarySize);
}
