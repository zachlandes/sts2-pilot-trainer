namespace EncounterIntel;

// Only inspected side-effect-free reference getters on the supported build
internal static class CatalogueBindings
{
    private static readonly IReadOnlyDictionary<string, string> Getters = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Aeonglass"] = "EbbDamage EbbBlock EyeLasersDamage EyeLasersRepeat",
        ["AssassinRubyRaider"] = "KillshotDamage",
        ["AxeRubyRaider"] = "SwingDamage SwingBlock BigSwingDamage",
        ["Axebot"] = "HammerUppercutDamage",
        ["BowlbugEgg"] = "BiteDamage",
        ["BowlbugNectar"] = "ThrashDamage BuffStrengthGain",
        ["BowlbugRock"] = "HeadbuttDamage",
        ["BowlbugSilk"] = "ThrashDamage",
        ["BruteRubyRaider"] = "BeatDamage",
        ["Byrdonis"] = "SwoopDamage",
        ["CalcifiedCultist"] = "DarkStrikeDamage",
        ["CeremonialBeast"] = "PlowDamage PlowStrength StompDamage CrushDamage CrushStrength",
        ["Chomper"] = "ClampDamage",
        ["CorpseSlug"] = "WhipSlapDamage WhipSlapRepeat GlompDamage",
        ["CrossbowRubyRaider"] = "FireDamage",
        ["Crusher"] = "ThrashDamage EnlargingStrikeDamage AdaptStrengthGain GuardedStrikeDamage",
        ["DampCultist"] = "DarkStrikeDamage",
        ["DecimillipedeSegment"] = "BulkDamage BulkStrength",
        ["DevotedSculptor"] = "SavageDamage",
        ["Exoskeleton"] = "SkitterDamage SkitterRepeats MandiblesDamage",
        ["Fabricator"] = "FabricatingStrikeDamage DisintegrateDamage",
        ["FakeMerchantMonster"] = "SwipeDamage ThrowRelicDamage",
        ["FlailKnight"] = "FlailDamage RamDamage",
        ["Flyconid"] = "SmashDamage",
        ["Fogmog"] = "HeadbuttDamage",
        ["FossilStalker"] = "LatchDamage TackleDamage LashDamage LashRepeat",
        ["FrogKnight"] = "TongueLashDamage StrikeDownEvilDamage BeetleChargeDamage",
        ["FuzzyWurmCrawler"] = "AcidGoopDamage",
        ["GasBomb"] = "ExplodeDamage",
        ["GlobeHead"] = "ShockingSlapDamage ThunderStrikeDamage GalvanicBurstDamage",
        ["GremlinMerc"] = "HeheDamage",
        ["HauntedShip"] = "SwipeDamage StompDamage StompRepeat",
        ["HunterKiller"] = "BiteDamage PunctureDamage",
        ["InfestedPrism"] = "JabDamage RadiateDamage RadiateBlock WhirlwindDamage WhirlwindRepeat PulsateBlock",
        ["Inklet"] = "JabDamage PiercingGazeDamage",
        ["KinFollower"] = "QuickSlashDamage BoomerangDamage",
        ["KinPriest"] = "OrbOfFrailtyDamage OrbOfWeaknessDamage",
        ["KnowledgeDemon"] = "SlapDamage KnowledgeOverwhelmingDamage PonderDamage PonderStrength",
        ["LagavulinMatriarch"] = "SlashDamage DisembowelDamage DisembowelRepeat Slash2Damage Slash2Block",
        ["LeafSlimeS"] = "TackleDamage",
        ["LivingFog"] = "AdvancedGasDamage BloatDamage SuperGasBlastDamage",
        ["LivingShield"] = "ShieldSlamDamage SmashDamage",
        ["LouseProgenitor"] = "PounceDamage",
        ["MagiKnight"] = "PowerShieldDamage PowerShieldBlock",
        ["Mawler"] = "RipAndTearDamage ClawDamage",
        ["MechaKnight"] = "ChargeDamage HeavyCleaveDamage",
        ["Myte"] = "BiteDamage SuckDamage SuckStrength",
        ["Nibbit"] = "ButtDamage HissStrengthGain",
        ["Ovicopter"] = "SmashDamage TenderizerDamage",
        ["OwlMagistrate"] = "PeckAssaultDamage VerdictDamage",
        ["Parafright"] = "SlamDamage",
        ["PhantasmalGardener"] = "BiteDamage LashDamage FlailDamage FlailRepeat EnlargeStr",
        ["PhrogParasite"] = "LashDamage",
        ["PunchConstruct"] = "FastPunchDamage FastPunchRepeat StrongPunchDamage",
        ["Queen"] = "OffWithYourHeadDamage ExecutionDamage",
        ["Rocket"] = "TargetingReticleDamage PrecisionBeamDamage ChargeUpStrengthGain LaserDamage",
        ["ScrollOfBiting"] = "ChompDamage ChewDamage",
        ["Seapunk"] = "SeaKickDamage SpinningKickDamage SpinningKickRepeat",
        ["SewerClam"] = "JetDamage",
        ["ShrinkerBeetle"] = "ChompDamage StompDamage",
        ["SkulkingColony"] = "ZoomDamage InertiaDamage",
        ["SlimedBerserker"] = "SmotherDamage",
        ["SlitheringStrangler"] = "ThwackDamage LashDamage",
        ["SludgeSpinner"] = "OilSprayDamage SlamDamage RageDamage",
        ["SlumberingBeetle"] = "RolloutDamage",
        ["SneakyGremlin"] = "TackleDamage",
        ["SoulFysh"] = "DeGasDamage GazeDamage ScreamDamage",
        ["SoulNexus"] = "SoulBurnDamage MaelstromDamage MaelstromRepeat DrainLifeDamage",
        ["SpectralKnight"] = "SoulSlashDamage SoulFlameDamage",
        ["Stabbot"] = "StabDamage",
        ["TerrorEel"] = "CrashDamage",
        ["TestSubject"] = "BiteDamage SkullBashDamage MultiClawDamage BigPounceDamage BurningGrowlStrengthGain",
        ["TheInsatiable"] = "ThrashDamage SalivateStrength",
        ["TheLost"] = "EyeLasersDamage",
        ["TheObscura"] = "PiercingGazeDamage HardeningStrikeDamage HardeningStrikeBlock",
        ["ThievingHopper"] = "HatTrickDamage NabDamage",
        ["Toadpole"] = "SpikeSpitDamage SpikeSpitRepeat WhirlDamage",
        ["TorchHeadAmalgam"] = "StrongTackleDamage TackleDamage WeakTackleDamage",
        ["ToughEgg"] = "NibbleDamage",
        ["Tunneler"] = "BiteDamage",
        ["TwigSlimeS"] = "TackleDamage",
        ["TwoTailedRat"] = "ScratchDamage DiseaseBiteDamage",
        ["Vantom"] = "InkBlotDamage InkyLanceDamage DismemberDamage",
        ["VineShambler"] = "SwipeDamage GraspingVinesDamage ChompDamage",
        ["WaterfallGiant"] = "StompDamage RamDamage PressureUpDamage",
        ["Zapbot"] = "ZapDamage",
    };

    public static string? Find(string model, string wikiName, string? move, string kind)
    {
        if (model.StartsWith("DecimillipedeSegment", StringComparison.Ordinal)) model = "DecimillipedeSegment";
        if (move is null || !Getters.TryGetValue(model, out var getters)) return null;
        if (model == "TestSubject" && kind == "damage" && move == "Multi-Claw" && wikiName != "Test Subject (Phase 2)") return null;
        var suffixes = kind switch
        {
            "damage" => new[] { "Damage" },
            "block" => ["Block"],
            "strength" => ["Strength", "StrengthGain", "Str", "StrGain"],
            "hits" => ["Repeat", "Repeats", "Times"],
            _ => [],
        };
        var candidates = getters.Split(' ').Where(getter => suffixes.Any(suffix => Normalize(getter) == Normalize(move + suffix))).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static string Normalize(string value) => string.Concat(value.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
}
