using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// What a reward on the loot screen is, in the names the format records it under.
///
/// One reader for the driver and the recorder, so the kind a replay looks for and
/// the kind a recording writes cannot come to mean different rewards. The card reward
/// is named too, as the kind no <see cref="ActionVerb.ClaimReward"/> may claim, so a
/// refusal can say what was on offer.
/// </summary>
public static class LootRewards
{
    /// <summary>The kind the format names a card reward by in a description only; it
    /// is taken with <see cref="ActionVerb.TakeCard"/>, never claimed.</summary>
    public const string CardRewardKind = "card";

    public static string KindOf(Reward reward) => reward switch
    {
        GoldReward => RewardKinds.Gold,
        PotionReward => RewardKinds.Potion,
        RelicReward => RewardKinds.Relic,
        CardRemovalReward => RewardKinds.CardRemoval,
        SpecialCardReward => RewardKinds.SpecialCard,
        CardReward => CardRewardKind,
        _ => reward.GetType().Name,
    };

    /// <summary>What a reward offers, for the kinds that name a thing, or null.</summary>
    public static string? IdOf(Reward reward) => reward switch
    {
        RelicReward relic => relic.Relic?.Id.ToString(),
        SpecialCardReward special => SpecialCardOf(special)?.Id.ToString(),
        _ => null,
    };

    /// <summary>
    /// The fixed card a special reward adds, which the reward keeps private and
    /// describes only through localized text. Read by name and refused loudly when a
    /// build no longer has it, because a card nobody can name is a claim nobody can
    /// check.
    /// </summary>
    private static CardModel? SpecialCardOf(SpecialCardReward reward)
    {
        var field = typeof(SpecialCardReward).GetField(
            "_card", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new EngineException(
                "SpecialCardReward has no _card on this build, so a special-card reward cannot be named and a " +
                "claim of one cannot be checked.");
        return field.GetValue(reward) as CardModel;
    }
}
