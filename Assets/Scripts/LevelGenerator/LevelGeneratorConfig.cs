using System.Collections.Generic;
using UnityEngine;

// 1. THE VOCABULARY
public enum UnlockableFeature
{
    None = 0,
    Colors_2,
    Colors_3,
    BlockedCell,
    Colors_4,
    Pipes,
    Colors_5,
    IceLayers,
    Colors_6,
    LinkedUnits,
    Colors_7,
    Crates,
    Colors_8,
    LocksAndKeys,
    Colors_9,
    Colors_10
}

// 2. THE RULEBOOK
public class LevelGeneratorConfig
{
    public const int TOTAL_LEVELS = 500;
    public const int MAX_DIFFICULTY_LEVEL = 100;

    public const int START_MIN_GRID = 2;
    public const int START_MAX_GRID = 3;

    public const int END_MIN_GRID = 5;
    public const int END_MAX_GRID = 8;

    private static readonly UnlockableFeature[] FeatureProgression = new UnlockableFeature[]
    {
        UnlockableFeature.Colors_2,
        UnlockableFeature.Colors_3,
        UnlockableFeature.BlockedCell,
        UnlockableFeature.Colors_4,
        UnlockableFeature.Pipes,
        UnlockableFeature.Colors_5,
        UnlockableFeature.IceLayers,
        UnlockableFeature.Colors_6,
        UnlockableFeature.LinkedUnits,
        UnlockableFeature.Colors_7,
        UnlockableFeature.Crates,
        UnlockableFeature.Colors_8,
        UnlockableFeature.LocksAndKeys,
        UnlockableFeature.Colors_9,
        UnlockableFeature.Colors_10
    };

    public class LevelRuleset
    {
        public int MaxColors;
        public List<UnlockableFeature> AllowedMechanics;
        public int MinGridSize;
        public int MaxGridSize;
        public float TargetWinRate;
        public float WinRateTolerance;
    }

    // --- NEW: Tracks exactly which level a feature first appears ---
    private static Dictionary<UnlockableFeature, int> _featureDebutLevels = null;
    public static Dictionary<UnlockableFeature, int> FeatureDebutLevels
    {
        get
        {
            if (_featureDebutLevels == null)
            {
                _featureDebutLevels = new Dictionary<UnlockableFeature, int>();
                for (int i = 1; i <= MAX_DIFFICULTY_LEVEL; i++)
                {
                    var rules = GetRulesForLevel(i);
                    foreach (var mech in rules.AllowedMechanics)
                    {
                        if (!_featureDebutLevels.ContainsKey(mech))
                            _featureDebutLevels[mech] = i; // Store the exact debut level
                    }
                }
            }
            return _featureDebutLevels;
        }
    }

    public static LevelRuleset GetRulesForLevel(int levelIndex)
    {
        LevelRuleset rules = new LevelRuleset
        {
            AllowedMechanics = new List<UnlockableFeature>(),
            WinRateTolerance = 0.05f
        };

        int clampedLevel = Mathf.Min(levelIndex, MAX_DIFFICULTY_LEVEL);
        float progress = (float)clampedLevel / MAX_DIFFICULTY_LEVEL;

        rules.MinGridSize = Mathf.RoundToInt(Mathf.Lerp(START_MIN_GRID, END_MIN_GRID, progress));
        rules.MaxGridSize = Mathf.RoundToInt(Mathf.Lerp(START_MAX_GRID, END_MAX_GRID, progress));

        rules.TargetWinRate = Mathf.Lerp(1.0f, 0.50f, progress);
        rules.MaxColors = 2; 

        int unlockedSteps = Mathf.FloorToInt(Mathf.Lerp(1, FeatureProgression.Length, progress));

        for (int i = 0; i < unlockedSteps; i++)
        {
            UnlockableFeature feature = FeatureProgression[i];
            if (feature.ToString().StartsWith("Colors_"))
            {
                rules.MaxColors = int.Parse(feature.ToString().Split('_')[1]);
            }
            else 
            {
                rules.AllowedMechanics.Add(feature);
            }
        }
        return rules;
    }
}