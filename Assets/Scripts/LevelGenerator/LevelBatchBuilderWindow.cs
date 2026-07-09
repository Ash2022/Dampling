using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

public class LevelBatchBuilderWindow : EditorWindow
{
    private int startLevel = 1;
    private int endLevel = 100;
    private int levelStepInterval = 1; // NEW: Skip levels for rapid generation
    private string outputFolderPath = "Assets/Resources/BakedLevels";

    private bool isProcessing = false;
    private int currentProcessingLevel = 1;
    private int currentAttempt = 0;
    private int maxAttemptsPerLevel = 50;
    private const int DUMPLINGS_PER_UNIT = 9;

    private GameLevelSchema bestCandidateLevel;
    private float bestCandidateDiff = float.MaxValue;
    private float bestCandidateWinRate = 0f;

    private DamplingSimulationAgent botAgent;
    private System.Random rng;

    private readonly string[] MasterColorPalette = {
        "Color_0", "Color_1", "Color_2", "Color_3", "Color_4",
        "Color_5", "Color_6", "Color_7", "Color_8", "Color_9"
    };

    [MenuItem("Tools/Level Batch Builder")]
    public static void ShowWindow() { GetWindow<LevelBatchBuilderWindow>("Level Builder"); }

    private void OnEnable() { EditorApplication.update += OnUpdateTick; }
    private void OnDisable() { EditorApplication.update -= OnUpdateTick; if (isProcessing) CancelProcessing(); }

    private void OnGUI()
    {
        GUILayout.Label("Dynamic Level Factory", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        GUI.enabled = !isProcessing;

        startLevel = EditorGUILayout.IntField("Start Level", startLevel);
        endLevel = EditorGUILayout.IntField("End Level", endLevel);
        levelStepInterval = Mathf.Max(1, EditorGUILayout.IntField("Step Interval (Skip X)", levelStepInterval));

        maxAttemptsPerLevel = Mathf.Max(1, EditorGUILayout.IntField("Max Attempts Per Level", maxAttemptsPerLevel));

        EditorGUILayout.BeginHorizontal();
        outputFolderPath = EditorGUILayout.TextField("Output Path", outputFolderPath);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets", "");
            if (!string.IsNullOrEmpty(path)) outputFolderPath = path;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        if (!isProcessing && GUILayout.Button("Generate Batch", GUILayout.Height(40))) StartProcessing();
        else if (isProcessing) { GUI.enabled = true; if (GUILayout.Button("CANCEL GENERATION", GUILayout.Height(40))) CancelProcessing(); }

        if (isProcessing)
        {
            EditorGUILayout.Space();
            GUILayout.Label("--- LIVE PROGRESS ---", EditorStyles.boldLabel);
            GUILayout.Label($"Baking Level: {currentProcessingLevel} / {endLevel}");
            GUILayout.Label($"Attempt: {currentAttempt} / {maxAttemptsPerLevel}");
            Repaint();
        }
    }

    private void StartProcessing()
    {
        if (startLevel > endLevel) return;
        if (!Directory.Exists(outputFolderPath)) Directory.CreateDirectory(outputFolderPath);
        botAgent = new DamplingSimulationAgent();
        rng = new System.Random();
        currentProcessingLevel = startLevel;
        currentAttempt = 0;
        bestCandidateLevel = null;
        bestCandidateDiff = float.MaxValue;
        bestCandidateWinRate = 0f;
        isProcessing = true;
    }

    private void CancelProcessing()
    {
        isProcessing = false;
        EditorUtility.ClearProgressBar();
        Debug.LogWarning("Level Generation Canceled.");
    }

    private void OnUpdateTick()
    {
        if (!isProcessing) return;

        // Accurate math for the step interval progress bar
        float totalLevels = Mathf.Ceil((endLevel - startLevel + 1f) / levelStepInterval);
        float levelsProcessed = (currentProcessingLevel - startLevel) / (float)levelStepInterval;
        float attemptFraction = ((float)currentAttempt / maxAttemptsPerLevel) * (1f / totalLevels);

        EditorUtility.DisplayProgressBar("Baking Levels...", $"Calibrating Level {currentProcessingLevel} (Attempt {currentAttempt})", (levelsProcessed / totalLevels) + attemptFraction);

        ProcessSingleAttempt();

        if (currentProcessingLevel > endLevel)
        {
            isProcessing = false;
            EditorUtility.ClearProgressBar();
            Debug.Log("<color=green><b>BATCH GENERATION COMPLETE!</b></color>");
        }
    }

    private void ProcessSingleAttempt()
    {
        currentAttempt++;
        var rules = LevelGeneratorConfig.GetRulesForLevel(currentProcessingLevel);
        GameLevelSchema candidate = GenerateCandidateLevel(rules, currentProcessingLevel);

        float progressToMax = Mathf.Clamp01((float)currentProcessingLevel / LevelGeneratorConfig.MAX_DIFFICULTY_LEVEL);
        float currentTolerance = Mathf.Lerp(0.05f, 0.30f, progressToMax);

        // 1. EARLY EXIT CULL
        var earlyReport = botAgent.RunBatchSimulation(candidate, 20);
        float earlyWinRate = earlyReport.WinRatePercentage / 100f;

        bool isHopeless = Mathf.Abs(rules.TargetWinRate - earlyWinRate) > (currentTolerance * 2f);

        // 2. FULL STRESS TEST (Only if not hopeless)
        float actualWinRate = 0f;
        if (!isHopeless)
        {
            var report = botAgent.RunBatchSimulation(candidate, 200);
            actualWinRate = report.WinRatePercentage / 100f;

            float distanceFromTarget = Mathf.Abs(rules.TargetWinRate - actualWinRate);
            if (distanceFromTarget < bestCandidateDiff)
            {
                bestCandidateDiff = distanceFromTarget;
                bestCandidateLevel = candidate;
                bestCandidateWinRate = actualWinRate;
            }

            // SUCCESS
            if (distanceFromTarget <= currentTolerance)
            {
                Debug.Log($"<color=cyan>Level {currentProcessingLevel}</color> baked. [Target: {rules.TargetWinRate:P0} | Actual: {actualWinRate:P0}]");
                SaveLevelToJson(candidate, currentProcessingLevel, outputFolderPath, rules.MaxColors, actualWinRate,rules.TargetWinRate);
                MoveToNextLevel();
                return;
            }
        }

        // 3. FAIL CONDITION (Hit Limit)
        // This now triggers regardless of whether the level was "culled" or just failed the stress test
        if (currentAttempt >= maxAttemptsPerLevel)
        {
            Debug.LogWarning($"<color=yellow>Level {currentProcessingLevel}</color> timeout. Saving BEST attempt. [Target: {rules.TargetWinRate:P0} | Actual: {bestCandidateWinRate:P0}]");

            // Fallback: If bestCandidateLevel is null (because every attempt was "hopeless"), use the last candidate
            SaveLevelToJson(bestCandidateLevel ?? candidate, currentProcessingLevel, outputFolderPath, rules.MaxColors, bestCandidateWinRate,rules.TargetWinRate);

            MoveToNextLevel();
            return;
        }
    }

    private void MoveToNextLevel()
    {
        currentProcessingLevel += levelStepInterval; // Move by step amount
        currentAttempt = 0;
        bestCandidateDiff = float.MaxValue;
        bestCandidateLevel = null;
    }

    private GameLevelSchema GenerateCandidateLevel(LevelGeneratorConfig.LevelRuleset rules, int levelIndex)
    {
        int width = rng.Next(rules.MinGridSize, rules.MaxGridSize + 1);
        int height = rng.Next(rules.MinGridSize, rules.MaxGridSize + 1);

        GameLevelSchema level = new GameLevelSchema
        {
            LevelId = levelIndex,
            LevelName = $"Generated_{levelIndex}",
            ConveyorBeltMaxCapacity = 28,
            Grid = new GameLevelSchema.GridTopology { Columns = width, Rows = height, Matrix = new List<GameLevelSchema.CellNode>() },
            ResolutionQueues = new List<List<GameLevelSchema.ContainerData>>()
        };

        List<string> activeColors = MasterColorPalette.Take(rules.MaxColors).ToList();
        Dictionary<string, int> totalUnitColorCounts = new Dictionary<string, int>();
        foreach (var c in activeColors) totalUnitColorCounts[c] = 0;

        int globalUnitIdCounter = 0;
        float blockedCellChance = (width * height >= 36) ? 0.25f : 0.1f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var node = new GameLevelSchema.CellNode
                {
                    Position = new GameLevelSchema.Coordinate(x, y),
                    IsPlayablePath = true

                };

                // --- CHANGE: Added condition to ensure BlockedCell is never on the bottom row (y == height - 1)
                if (rules.AllowedMechanics.Contains(UnlockableFeature.BlockedCell) && y < (height - 1) && rng.NextDouble() < blockedCellChance)
                {
                    node.IsPlayablePath = false;
                }
                else
                {
                    string randomColor = activeColors[rng.Next(activeColors.Count)];
                    var newUnit = new GameLevelSchema.GridUnit
                    {
                        UnitId = globalUnitIdCounter++,
                        IceLayers = 0,
                        InteriorContents = new List<GameLevelSchema.DumplingItem>()
                    };

                    for (int d = 0; d < DUMPLINGS_PER_UNIT; d++)
                        newUnit.InteriorContents.Add(new GameLevelSchema.DumplingItem { ColorId = randomColor });

                    node.OccupyingUnit = newUnit;
                    totalUnitColorCounts[randomColor]++;
                }
                level.Grid.Matrix.Add(node);
            }
        }

        // Passed LevelIndex to track feature Debuts
        ApplyMechanics(level, rules, activeColors, totalUnitColorCounts, width, height, ref globalUnitIdCounter, levelIndex);
        GenerateResolutionQueues(level, totalUnitColorCounts);

        return level;
    }

    private void ApplyMechanics(GameLevelSchema level, LevelGeneratorConfig.LevelRuleset rules, List<string> activeColors, Dictionary<string, int> colorTracker, int width, int height, ref int unitIdCounter, int levelIndex)
    {
        // Helper function for the "Debut Guarantee & Probability Ramp"
        bool ShouldApply(UnlockableFeature feature)
        {
            if (!rules.AllowedMechanics.Contains(feature)) return false;
            int debut = LevelGeneratorConfig.FeatureDebutLevels.ContainsKey(feature) ? LevelGeneratorConfig.FeatureDebutLevels[feature] : 1;

            if (levelIndex == debut) return true; // 100% Guaranteed on Debut

            // Starts at 25% chance and slowly ramps to 85% as game progresses
            float chance = Mathf.Clamp(0.25f + ((levelIndex - debut) * 0.015f), 0.25f, 0.85f);
            return rng.NextDouble() < chance;
        }

        // PIPES - Volume Scaled Multiple
        if (ShouldApply(UnlockableFeature.Pipes))
        {
            int maxPipes = (width * height >= 36) ? 3 : (width * height >= 16) ? 2 : 1;
            int pipeCount = rng.Next(1, maxPipes + 1);

            // Pick non-overlapping columns for pipes
            var availableX = Enumerable.Range(0, width).OrderBy(x => rng.Next()).ToList();

            for (int i = 0; i < Math.Min(pipeCount, availableX.Count); i++)
            {
                int pipeX = availableX[i];
                var pipeNode = level.Grid.Matrix.FirstOrDefault(n => n.Position.X == pipeX && n.Position.Y > 0);

                if (pipeNode != null && pipeNode.IsPlayablePath)
                {
                    // --- THE BUG FIX: Decrement tracker before destroying the unit ---
                    if (pipeNode.OccupyingUnit != null && pipeNode.OccupyingUnit.InteriorContents.Count > 0)
                    {
                        // Grab the color id of the dumpling inside the unit we are about to erase
                        string colorToDeduct = pipeNode.OccupyingUnit.InteriorContents[0].ColorId;
                        if (colorTracker.ContainsKey(colorToDeduct))
                        {
                            colorTracker[colorToDeduct]--;
                        }
                    }

                    pipeNode.OccupyingUnit = null; // Safe to erase now
                    int emissionCount = rng.Next(3, 6);

                    pipeNode.ContinuousPipe = new GameLevelSchema.PipeGenerator
                    {
                        MaxTotalEmissions = emissionCount,
                        ReservoirQueue = new List<GameLevelSchema.GridUnit>()
                    };

                    for (int e = 0; e < emissionCount; e++)
                    {
                        string randomColor = activeColors[rng.Next(activeColors.Count)];
                        var queuedUnit = new GameLevelSchema.GridUnit
                        {
                            UnitId = unitIdCounter++,
                            IceLayers = 0,
                            InteriorContents = new List<GameLevelSchema.DumplingItem>()
                        };

                        for (int d = 0; d < DUMPLINGS_PER_UNIT; d++)
                            queuedUnit.InteriorContents.Add(new GameLevelSchema.DumplingItem { ColorId = randomColor });

                        pipeNode.ContinuousPipe.ReservoirQueue.Add(queuedUnit);
                        colorTracker[randomColor]++;
                    }
                }
            }
        }

        // ICE LAYERS
        if (ShouldApply(UnlockableFeature.IceLayers))
        {
            var activeUnits = level.Grid.Matrix.Where(n => n.OccupyingUnit != null).ToList();
            if (activeUnits.Count > 0)
            {
                int numIced = rng.Next(1, activeUnits.Count / 3 + 1);
                for (int i = 0; i < numIced; i++) activeUnits[rng.Next(activeUnits.Count)].OccupyingUnit.IceLayers = rng.Next(1, 3);
            }
        }

        // LINKED UNITS - Volume Scaled Multiple Pairs
        if (ShouldApply(UnlockableFeature.LinkedUnits))
        {
            // Shuffle all units so we can pick unique pairs sequentially
            var units = level.Grid.Matrix.Where(n => n.OccupyingUnit != null).Select(n => n.OccupyingUnit).OrderBy(u => rng.Next()).ToList();

            // 1 pair per 12 tiles (e.g. 3 pairs on a 6x6)
            int pairsToMake = Math.Max(1, (width * height) / 12);

            for (int i = 0; i < pairsToMake && (i * 2 + 1) < units.Count; i++)
            {
                var u1 = units[i * 2];
                var u2 = units[i * 2 + 1];
                u1.LinkedUnitIds.Add(u2.UnitId);
                u2.LinkedUnitIds.Add(u1.UnitId);
            }
        }

        // LOCKS AND KEYS - Row Biased & Volume Scaled
        if (ShouldApply(UnlockableFeature.LocksAndKeys))
        {
            var activeNodes = level.Grid.Matrix.Where(n => n.OccupyingUnit != null).ToList();

            // Strict split: Locks on Top, Keys on Bottom
            var topHalf = activeNodes.Where(n => n.Position.Y < height / 2.0f).Select(n => n.OccupyingUnit).OrderBy(u => rng.Next()).ToList();
            var bottomHalf = activeNodes.Where(n => n.Position.Y >= height / 2.0f).Select(n => n.OccupyingUnit).OrderBy(u => rng.Next()).ToList();

            int pairsToMake = Math.Max(1, (width * height) / 20);

            for (int i = 0; i < pairsToMake && i < topHalf.Count && i < bottomHalf.Count; i++)
            {
                var lockUnit = topHalf[i];
                var keyUnit = bottomHalf[i];
                lockUnit.ExplicitlyBlockedByUnitIds.Add(keyUnit.UnitId);
            }
        }
    }

    private void GenerateResolutionQueues(GameLevelSchema level, Dictionary<string, int> totalUnitColorCounts)
    {
        var flatContainersList = new List<GameLevelSchema.ContainerData>();

        foreach (var kvp in totalUnitColorCounts)
        {
            string color = kvp.Key;
            int totalDumplingsOfColor = kvp.Value * DUMPLINGS_PER_UNIT;

            while (totalDumplingsOfColor > 0)
            {
                int capacity = Math.Min(totalDumplingsOfColor, 3);
                flatContainersList.Add(new GameLevelSchema.ContainerData
                {
                    ColorId = color,
                    Capacity = capacity,
                    FilledSlotsCount = 0
                });
                totalDumplingsOfColor -= capacity;
            }
        }

        flatContainersList = flatContainersList.OrderBy(x => rng.Next()).ToList();

        int numQueues = 4;
        for (int i = 0; i < numQueues; i++) level.ResolutionQueues.Add(new List<GameLevelSchema.ContainerData>());
        for (int i = 0; i < flatContainersList.Count; i++) level.ResolutionQueues[i % numQueues].Add(flatContainersList[i]);

        int strictIdCounter = 0;
        foreach (var queue in level.ResolutionQueues)
        {
            foreach (var container in queue) container.Id = strictIdCounter++;
        }
    }

    // NEW: Accepts maxColors and WinRate for the filename formatting
    private void SaveLevelToJson(GameLevelSchema level, int index, string path, int maxColors, float actualWinRate, float targetWinRate)
    {
        int actualWinRateInt = Mathf.RoundToInt(actualWinRate * 100);
        int targetWinRateInt = Mathf.RoundToInt(targetWinRate * 100);

        // Tally features directly from the finalized schema
        int blockersCount = level.Grid.Matrix.Count(n => !n.IsPlayablePath);
        int pipesCount = level.Grid.Matrix.Count(n => n.ContinuousPipe != null);

        int linksCount = level.Grid.Matrix
            .Where(n => n.OccupyingUnit != null)
            .Sum(n => n.OccupyingUnit.LinkedUnitIds.Count) / 2; // Divided by 2 since links are bidirectional references

        int iceCount = level.Grid.Matrix
            .Where(n => n.OccupyingUnit != null && n.OccupyingUnit.IceLayers > 0)
            .Sum(n => n.OccupyingUnit.IceLayers);

        int locksCount = level.Grid.Matrix
            .Where(n => n.OccupyingUnit != null)
            .Sum(n => n.OccupyingUnit.ExplicitlyBlockedByUnitIds.Count);

        // Format: Lvl_001_WR[Actual]_[Target]_Cols_3_B_2_P_1_L_2_I_3_K_1.json
        string fileName = $"Lvl_{index:000}_WR{actualWinRateInt}_{targetWinRateInt}_Cols_{maxColors}_B_{blockersCount}_P_{pipesCount}_L_{linksCount}_I_{iceCount}_K_{locksCount}.json";
        string fullPath = Path.Combine(path, fileName);

        var settings = new JsonSerializerSettings { Formatting = Formatting.Indented };
        string json = JsonConvert.SerializeObject(level, settings);
        File.WriteAllText(fullPath, json);
    }
}