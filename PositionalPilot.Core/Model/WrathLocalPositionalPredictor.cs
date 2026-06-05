namespace PositionalPilot.Core.Model;

public static class WrathLocalPositionalPredictor
{
    private const uint JobMnk = 20;
    private const uint JobDrg = 22;
    private const uint JobNin = 30;
    private const uint JobSam = 34;
    private const uint JobRpr = 39;
    private const uint JobVpr = 41;

    private const uint MnkTwinSnakes = 61;
    private const uint MnkTrueStrike = 54;
    private const uint MnkRisingRaptor = 36945;
    private const uint MnkDemolish = 66;
    private const uint MnkSnapPunch = 56;

    private const uint DrgFullThrust = 84;
    private const uint DrgDisembowel = 87;
    private const uint DrgChaosThrust = 88;
    private const uint DrgFangAndClaw = 3554;
    private const uint DrgWheelingThrust = 3556;
    private const uint DrgChaoticSpring = 25772;
    private const uint DrgHeavensThrust = 25771;
    private const uint DrgSpiralBlow = 36955;

    private const uint NinGustSlash = 2242;
    private const uint NinAeolianEdge = 2255;
    private const uint NinArmorCrush = 3563;
    private const uint NinTrickAttack = 2258;
    private const uint NinKunaisBane = 36958;
    private const uint NinTrickAttackDebuff = 3254;
    private const uint NinKunaisBaneDebuff = 3906;

    private const uint SamJinpu = 7478;
    private const uint SamShifu = 7479;
    private const uint SamGekko = 7481;
    private const uint SamKasha = 7482;
    private const uint SamMeikyoShisuiStatus = 1233;

    private const uint RprSoulReaverStatus = 2587;
    private const uint RprEnhancedGibbetStatus = 2588;
    private const uint RprEnhancedGallowsStatus = 2589;
    private const uint RprExecutionerStatus = 3858;

    private const uint VprFlankstingStrike = 34610;
    private const uint VprFlanksbaneFang = 34611;
    private const uint VprHindstingStrike = 34612;
    private const uint VprHindsbaneFang = 34613;
    private const uint VprVicewinder = 34620;
    private const uint VprHuntersCoil = 34621;
    private const uint VprSwiftskinsCoil = 34622;
    private const uint VprHuntersInstinct = 3668;
    private const uint VprSwiftscaled = 3669;

    public static WrathLocalPrediction Predict(WrathLocalPredictionSnapshot snapshot)
    {
        var stale = IsStale(snapshot);
        var sourceSuffix = stale ? " stale" : string.Empty;
        var prediction = snapshot.JobId switch
        {
            JobMnk => PredictMonk(snapshot),
            JobDrg => PredictDragoon(snapshot),
            JobNin => PredictNinja(snapshot),
            JobSam => PredictSamurai(snapshot),
            JobRpr => PredictReaper(snapshot),
            JobVpr => PredictViper(snapshot),
            _ => Unknown(snapshot, $"Wrath local predictor: unsupported job {snapshot.JobId}"),
        };

        if (prediction.Requirement is PositionalRequirement.Rear or PositionalRequirement.Flank && stale)
        {
            return prediction with
            {
                Requirement = PositionalRequirement.Unknown,
                IsFreshOrUsable = false,
                Source = prediction.Source + sourceSuffix,
            };
        }

        return prediction with { IsFreshOrUsable = !stale && prediction.Requirement is PositionalRequirement.Rear or PositionalRequirement.Flank };
    }

    private static WrathLocalPrediction PredictMonk(WrathLocalPredictionSnapshot s)
    {
        if (!IsAny(s.ComboActionId, MnkTwinSnakes, MnkTrueStrike, MnkRisingRaptor))
            return Unknown(s, "Wrath local predictor: MNK combo action not Coeurl setup");

        if (CanUse(s, MnkDemolish, 30) && s.MonkCoeurlFury == 0)
            return Known(s, PositionalRequirement.Rear, "Wrath MNK: Coeurl Fury empty, Demolish anticipated");
        if (CanUse(s, MnkSnapPunch, 6) && s.MonkCoeurlFury > 0)
            return Known(s, PositionalRequirement.Flank, "Wrath MNK: Coeurl Fury available, Snap Punch anticipated");

        return Unknown(s, "Wrath local predictor: MNK Coeurl branch ambiguous");
    }

    private static WrathLocalPrediction PredictDragoon(WrathLocalPredictionSnapshot s)
    {
        if (IsAny(s.ComboActionId, DrgDisembowel, DrgSpiralBlow) && CanUse(s, DrgChaosThrust, 50))
            return Known(s, PositionalRequirement.Rear, "Wrath DRG: Chaos/Chaotic Spring path");
        if (IsAny(s.ComboActionId, DrgChaosThrust, DrgChaoticSpring) && CanUse(s, DrgWheelingThrust, 64))
            return Known(s, PositionalRequirement.Rear, "Wrath DRG: Wheeling Thrust followup");
        if (IsAny(s.ComboActionId, DrgFullThrust, DrgHeavensThrust) && CanUse(s, DrgFangAndClaw, 56))
            return Known(s, PositionalRequirement.Flank, "Wrath DRG: Fang and Claw followup");

        return Unknown(s, "Wrath local predictor: DRG combo action unknown");
    }

    private static WrathLocalPrediction PredictNinja(WrathLocalPredictionSnapshot s)
    {
        if (s.ComboActionId != NinGustSlash)
            return Unknown(s, "Wrath local predictor: NIN combo action not Gust Slash");

        var vulnWindow = s.TargetStatusIds.Contains(NinTrickAttackDebuff) || s.TargetStatusIds.Contains(NinKunaisBaneDebuff);
        var armorCrushReady = CanUse(s, NinArmorCrush, 54);
        var aeolianReady = CanUse(s, NinAeolianEdge, 26);
        if (!armorCrushReady && aeolianReady)
            return Known(s, PositionalRequirement.Rear, "Wrath NIN: Aeolian Edge before Armor Crush level");
        if (armorCrushReady && s.NinjaKazematoi <= 0 && !vulnWindow)
            return Known(s, PositionalRequirement.Flank, "Wrath NIN: Kazematoi refresh Armor Crush");
        if (aeolianReady && (vulnWindow || s.ActionReadyIds.Contains(NinTrickAttack) || s.ActionReadyIds.Contains(NinKunaisBane)))
            return Known(s, PositionalRequirement.Rear, "Wrath NIN: burst window Aeolian Edge");

        return Unknown(s, "Wrath local predictor: NIN Gust Slash branch ambiguous");
    }

    private static WrathLocalPrediction PredictSamurai(WrathLocalPredictionSnapshot s)
    {
        if (s.ComboActionId == SamJinpu || (s.ComboActionId == 0 && s.FilteredWeaponskillOrSpellId == SamJinpu))
            return Known(s, PositionalRequirement.Rear, "Wrath SAM: Jinpu -> Gekko");
        if (s.ComboActionId == SamShifu || (s.ComboActionId == 0 && s.FilteredWeaponskillOrSpellId == SamShifu))
            return Known(s, PositionalRequirement.Flank, "Wrath SAM: Shifu -> Kasha");

        if (!s.EnableSamMeikyoAnticipation || !s.PlayerStatusIds.Contains(SamMeikyoShisuiStatus))
            return Unknown(s, "Wrath local predictor: SAM combo action unknown");

        if (CanUse(s, SamGekko, 30) && s.SamuraiHasGetsu == false && s.SamuraiHasKa == true)
            return Known(s, PositionalRequirement.Rear, "Wrath SAM: Meikyo Gekko anticipation");
        if (CanUse(s, SamKasha, 40) && s.SamuraiHasKa == false)
            return Known(s, PositionalRequirement.Flank, "Wrath SAM: Meikyo Kasha anticipation");

        return Unknown(s, "Wrath local predictor: SAM Meikyo branch ambiguous");
    }

    private static WrathLocalPrediction PredictReaper(WrathLocalPredictionSnapshot s)
    {
        var canGibbet = s.PlayerStatusIds.Contains(RprEnhancedGibbetStatus);
        var canGallows = s.PlayerStatusIds.Contains(RprEnhancedGallowsStatus);
        var hasReaverWindow = s.PlayerStatusIds.Contains(RprSoulReaverStatus) || s.PlayerStatusIds.Contains(RprExecutionerStatus);

        if (canGallows && !canGibbet)
            return Known(s, PositionalRequirement.Rear, "Wrath RPR: Gallows directional status");
        if (canGibbet && !canGallows)
            return Known(s, PositionalRequirement.Flank, "Wrath RPR: Gibbet directional status");
        if (!hasReaverWindow)
            return Unknown(s, "Wrath local predictor: RPR Soul Reaver/Executioner window missing");

        return Unknown(s, "Wrath local predictor: RPR directional branch ambiguous");
    }

    private static WrathLocalPrediction PredictViper(WrathLocalPredictionSnapshot s)
    {
        if (IsAny(s.ComboActionId, VprHindstingStrike, VprHindsbaneFang))
            return Known(s, PositionalRequirement.Rear, "Wrath VPR: hind combo path");
        if (IsAny(s.ComboActionId, VprFlankstingStrike, VprFlanksbaneFang))
            return Known(s, PositionalRequirement.Flank, "Wrath VPR: flank combo path");

        if (s.FilteredWeaponskillOrSpellId == VprVicewinder)
        {
            var hunters = Remaining(s, VprHuntersInstinct);
            var swiftscaled = Remaining(s, VprSwiftscaled);
            if (hunters > swiftscaled)
                return Known(s, PositionalRequirement.Flank, "Wrath VPR: Vicewinder Hunter's Coil followup");
            if (swiftscaled > hunters)
                return Known(s, PositionalRequirement.Rear, "Wrath VPR: Vicewinder Swiftskin's Coil followup");
        }

        var dreadCombo = s.ViperDreadCombo != 0 ? s.ViperDreadCombo : s.FilteredWeaponskillOrSpellId;
        if (dreadCombo == VprHuntersCoil)
            return Known(s, PositionalRequirement.Rear, "Wrath VPR: Hunter's Coil -> Swiftskin's Coil");
        if (dreadCombo == VprSwiftskinsCoil)
            return Known(s, PositionalRequirement.Flank, "Wrath VPR: Swiftskin's Coil -> Hunter's Coil");

        return Unknown(s, "Wrath local predictor: VPR branch unknown");
    }

    private static WrathLocalPrediction Known(WrathLocalPredictionSnapshot s, PositionalRequirement requirement, string source) =>
        new(s.RawActionId, s.FilteredWeaponskillOrSpellId, s.ComboActionId, source, requirement, true);

    private static WrathLocalPrediction Unknown(WrathLocalPredictionSnapshot s, string source) =>
        new(s.RawActionId, s.FilteredWeaponskillOrSpellId, s.ComboActionId, source, PositionalRequirement.Unknown, false);

    private static bool IsStale(WrathLocalPredictionSnapshot s)
    {
        if (s.ComboActionId != 0)
            return false;
        if (s.FilteredWeaponskillOrSpellId == 0 || s.FilteredWeaponskillOrSpellUpdatedAt == DateTime.MinValue)
            return false;

        return s.Now - s.FilteredWeaponskillOrSpellUpdatedAt > TimeSpan.FromMilliseconds(s.MaxAgeMs);
    }

    private static bool CanUse(WrathLocalPredictionSnapshot s, uint actionId, byte minimumLevel) =>
        s.PlayerLevel >= minimumLevel || s.ActionReadyIds.Contains(actionId);

    private static float Remaining(WrathLocalPredictionSnapshot s, uint statusId) =>
        s.PlayerStatusRemainingSeconds.TryGetValue(statusId, out var remaining) ? remaining : 0;

    private static bool IsAny(uint value, params uint[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (value == candidate)
                return true;
        }

        return false;
    }
}
