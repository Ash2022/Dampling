using System.Collections.Generic;
using UnityEngine;

// 1. THE VOCABULARY (Exact sequence requested)
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
    // --- MASTER CONFIGURATION CONSTANTS ---
    // Change this one number to stretch or compress the entire game's difficulty curve
    public const int TOTAL_LEVELS = 500;
    public const int MAX_DIFFICULTY_LEVEL = 100;

    // Grid Scaling Bounds
    public const int START_MIN_GRID = 2;
    public const int START_MAX_GRID = 3;

    public const int END_MIN_GRID = 5;
    public const int END_MAX_GRID = 8;

    // The strict order of progression
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

    public static LevelRuleset GetRulesForLevel(int levelIndex)
    {
        LevelRuleset rules = new LevelRuleset
        {
            AllowedMechanics = new List<UnlockableFeature>(),
            WinRateTolerance = 0.05f
        };

        int clampedLevel = Mathf.Min(levelIndex, MAX_DIFFICULTY_LEVEL);
        float progress = (float)clampedLevel / MAX_DIFFICULTY_LEVEL;

        // 1. SMOOTH GRID SCALING (No if/else blocks)
        rules.MinGridSize = Mathf.RoundToInt(Mathf.Lerp(START_MIN_GRID, END_MIN_GRID, progress));
        rules.MaxGridSize = Mathf.RoundToInt(Mathf.Lerp(START_MAX_GRID, END_MAX_GRID, progress));

        // 2. DIFFICULTY CURVE (100% down to 30%)
        rules.TargetWinRate = Mathf.Lerp(1.0f, 0.50f, progress);

        // 3. DYNAMIC FEATURE UNLOCKS
        rules.MaxColors = 2; // Absolute base fallback

        // Calculate how many features to unlock based on our progress percentage
        int unlockedSteps = Mathf.FloorToInt(Mathf.Lerp(1, FeatureProgression.Length, progress));

        for (int i = 0; i < unlockedSteps; i++)
        {
            UnlockableFeature feature = FeatureProgression[i];

            // Check if this feature is a color upgrade
            if (feature.ToString().StartsWith("Colors_"))
            {
                // Extracts the number (e.g. "Colors_6" -> 6)
                rules.MaxColors = int.Parse(feature.ToString().Split('_')[1]);
            }
            else // Otherwise, it's a mechanical board feature
            {
                rules.AllowedMechanics.Add(feature);
            }
        }

        return rules;
    }
}