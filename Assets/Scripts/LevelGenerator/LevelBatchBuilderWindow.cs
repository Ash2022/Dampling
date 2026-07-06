using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

public class LevelBatchBuilderWindow : EditorWindow
{
    // --- UI Configuration ---
    private int startLevel = 1;
    private int endLevel = 100;
    private string outputFolderPath = "Assets/Resources/BakedLevels";

    // --- State Machine Variables ---
    private bool isProcessing = false;
    private int currentProcessingLevel = 1;
    private int currentAttempt = 0;
    private const int MAX_ATTEMPTS_PER_LEVEL = 50;
    private const int DUMPLINGS_PER_UNIT = 9; // FIXED: Matches your data schema

    // --- Best Candidate Tracking ---
    private GameLevelSchema bestCandidateLevel;
    private float bestCandidateDiff = float.MaxValue;
    private float bestCandidateWinRate = 0f;

    // --- Core Systems ---
    private DamplingSimulationAgent botAgent;
    private System.Random rng;
    
    // FIXED: Exact string matches to your Unity assets
    private readonly string[] MasterColorPalette = { 
        "Color_0", "Color_1", "Color_2", "Color_3", "Color_4", 
        "Color_5", "Color_6", "Color_7", "Color_8", "Color_9" 
    };

    [MenuItem("Tools/Level Batch Builder")]
    public static void ShowWindow()
    {
        GetWindow<LevelBatchBuilderWindow>("Level Builder");
    }

    private void OnEnable() { EditorApplication.update += OnUpdateTick; }
    private void OnDisable() { EditorApplication.update -= OnUpdateTick; if (isProcessing) CancelProcessing(); }

    private void OnGUI()
    {
        GUILayout.Label("Dynamic Level Factory", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        GUI.enabled = !isProcessing;

        startLevel = EditorGUILayout.IntField("Start Level", startLevel);
        endLevel = EditorGUILayout.IntField("End Level", endLevel);
        
        EditorGUILayout.BeginHorizontal();
        outputFolderPath = EditorGUILayout.TextField("Output Path", outputFolderPath);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Output Folder", "Assets", "");
            if (!string.IsNullOrEmpty(path)) outputFolderPath = path;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        if (!isProcessing)
        {
            if (GUILayout.Button("Generate Batch", GUILayout.Height(40))) StartProcessing();
        }
        else
        {
            GUI.enabled = true; 
            if (GUILayout.Button("CANCEL GENERATION", GUILayout.Height(40))) CancelProcessing();
        }

        if (isProcessing)
        {
            EditorGUILayout.Space();
            GUILayout.Label("--- LIVE PROGRESS ---", EditorStyles.boldLabel);
            GUILayout.Label($"Baking Level: {currentProcessingLevel} / {endLevel}");
            GUILayout.Label($"Attempt: {currentAttempt} / {MAX_ATTEMPTS_PER_LEVEL}");
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
        
        // Reset best candidate tracking
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

        float totalLevels = (endLevel - startLevel) + 1;
        float progress = (currentProcessingLevel - startLevel) / totalLevels;
        float attemptFraction = ((float)currentAttempt / MAX_ATTEMPTS_PER_LEVEL) * (1f / totalLevels);
        
        EditorUtility.DisplayProgressBar("Baking Levels...", $"Calibrating Level {currentProcessingLevel} (Attempt {currentAttempt})", progress + attemptFraction);

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
        
        // --- OPTIMIZATION: Dynamic Tolerance Cone ---
        float progressToMax = Mathf.Clamp01((float)currentProcessingLevel / LevelGeneratorConfig.MAX_DIFFICULTY_LEVEL);
        float currentTolerance = Mathf.Lerp(0.05f, 0.20f, progressToMax); // 5% early game, up to 20% late game

        // --- OPTIMIZATION: Early Exit Cull ---
        // Run a tiny batch of 20 games first.
        var earlyReport = botAgent.RunBatchSimulation(candidate, 20);
        float earlyWinRate = earlyReport.WinRatePercentage / 100f;
        
        // If it's hopelessly far off (more than double the tolerance), instantly trash it and skip the deep test.
        if (Mathf.Abs(rules.TargetWinRate - earlyWinRate) > (currentTolerance * 2f))
        {
            return; // Exit out, letting the next frame handle Attempt++
        }

        // --- FULL STRESS TEST ---
        var report = botAgent.RunBatchSimulation(candidate, 200);
        float actualWinRate = report.WinRatePercentage / 100f;

        // --- BEST-FIT MEMORY ---
        float distanceFromTarget = Mathf.Abs(rules.TargetWinRate - actualWinRate);
        if (distanceFromTarget < bestCandidateDiff)
        {
            bestCandidateDiff = distanceFromTarget;
            bestCandidateLevel = candidate;
            bestCandidateWinRate = actualWinRate;
        }

        // --- SUCCESS CONDITION ---
        if (distanceFromTarget <= currentTolerance)
        {
            Debug.Log($"<color=cyan>Level {currentProcessingLevel}</color> baked in {currentAttempt} attempts. [Target: {rules.TargetWinRate:P0} | Actual: {actualWinRate:P0}]");
            SaveLevelToJson(candidate, currentProcessingLevel, outputFolderPath);
            
            MoveToNextLevel();
            return;
        }

        // --- FAIL CONDITION (Hit Limit) ---
        if (currentAttempt >= MAX_ATTEMPTS_PER_LEVEL)
        {
            Debug.LogWarning($"<color=yellow>Level {currentProcessingLevel}</color> timed out. Saving BEST attempt (Missed target by {bestCandidateDiff:P0}). [Target: {rules.TargetWinRate:P0} | Actual: {bestCandidateWinRate:P0}]");
            SaveLevelToJson(bestCandidateLevel, currentProcessingLevel, outputFolderPath);
            
            MoveToNextLevel();
            return;
        }
    }

    private void MoveToNextLevel()
    {
        currentProcessingLevel++;
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
            ConveyorBeltMaxCapacity = 28, // Matches your valid JSON
            Grid = new GameLevelSchema.GridTopology 
            { 
                Columns = width, Rows = height, Matrix = new List<GameLevelSchema.CellNode>() 
            },
            ResolutionQueues = new List<List<GameLevelSchema.ContainerData>>()
        };

        List<string> activeColors = MasterColorPalette.Take(rules.MaxColors).ToList();
        Dictionary<string, int> totalUnitColorCounts = new Dictionary<string, int>();
        foreach (var c in activeColors) totalUnitColorCounts[c] = 0;

        int globalUnitIdCounter = 0;

        // --- OPTIMIZATION: Large Board Density Constraint ---
        float blockedCellChance = 0.1f;
        if (width * height >= 36) blockedCellChance = 0.25f; // Choke points forced on huge boards

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var node = new GameLevelSchema.CellNode
                {
                    Position = new GameLevelSchema.Coordinate(x, y),
                    IsPlayablePath = true,
                    CrateDurability = 0
                };

                if (rules.AllowedMechanics.Contains(UnlockableFeature.BlockedCell) && rng.NextDouble() < blockedCellChance)
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

                    // FIXED: 9 Dumplings per unit
                    for (int d = 0; d < DUMPLINGS_PER_UNIT; d++)
                    {
                        newUnit.InteriorContents.Add(new GameLevelSchema.DumplingItem { ColorId = randomColor });
                    }
                    
                    node.OccupyingUnit = newUnit;
                    totalUnitColorCounts[randomColor]++;
                }
                level.Grid.Matrix.Add(node);
            }
        }

        ApplyMechanics(level, rules, activeColors, totalUnitColorCounts, width, height, ref globalUnitIdCounter);
        GenerateResolutionQueues(level, totalUnitColorCounts);

        return level;
    }

    private void ApplyMechanics(GameLevelSchema level, LevelGeneratorConfig.LevelRuleset rules, List<string> activeColors, Dictionary<string, int> colorTracker, int width, int height, ref int unitIdCounter)
    {
        // PIPES
        if (rules.AllowedMechanics.Contains(UnlockableFeature.Pipes))
        {
            int pipeCount = rng.Next(1, 3);
            for (int i = 0; i < pipeCount; i++)
            {
                int pipeX = rng.Next(0, width);
                var pipeNode = level.Grid.Matrix.FirstOrDefault(n => n.Position.X == pipeX && n.Position.Y == 0);
                
                if (pipeNode != null && pipeNode.IsPlayablePath)
                {
                    pipeNode.OccupyingUnit = null; 
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
                        {
                            queuedUnit.InteriorContents.Add(new GameLevelSchema.DumplingItem { ColorId = randomColor });
                        }
                        
                        pipeNode.ContinuousPipe.ReservoirQueue.Add(queuedUnit);
                        colorTracker[randomColor]++; 
                    }
                }
            }
        }

        // [Ice, Crates, LinkedUnits, LocksAndKeys logic remains identical to previous version...]
        // (Omitted for brevity, but they operate the same way without touching color math).
        
        if (rules.AllowedMechanics.Contains(UnlockableFeature.IceLayers))
        {
            var activeUnits = level.Grid.Matrix.Where(n => n.OccupyingUnit != null).ToList();
            if (activeUnits.Count > 0)
            {
                int numIced = rng.Next(1, activeUnits.Count / 3 + 1); 
                for (int i = 0; i < numIced; i++) activeUnits[rng.Next(activeUnits.Count)].OccupyingUnit.IceLayers = rng.Next(1, 3);
            }
        }

        if (rules.AllowedMechanics.Contains(UnlockableFeature.LinkedUnits))
        {
            var units = level.Grid.Matrix.Where(n => n.OccupyingUnit != null).Select(n => n.OccupyingUnit).ToList();
            if (units.Count >= 2)
            {
                var u1 = units[rng.Next(units.Count)];
                var u2 = units[rng.Next(units.Count)];
                if (u1.UnitId != u2.UnitId)
                {
                    u1.LinkedUnitIds.Add(u2.UnitId);
                    u2.LinkedUnitIds.Add(u1.UnitId);
                }
            }
        }
    }

    private void GenerateResolutionQueues(GameLevelSchema level, Dictionary<string, int> totalUnitColorCounts)
    {
        var flatContainersList = new List<GameLevelSchema.ContainerData>();

        // 1. Multiply tracked units by dumplings to get absolute total demand
        foreach (var kvp in totalUnitColorCounts)
        {
            string color = kvp.Key;
            int totalDumplingsOfColor = kvp.Value * DUMPLINGS_PER_UNIT;

            // 2. Create the raw containers (Capacity 3)
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

        // 3. Shuffle the containers BEFORE assigning IDs
        flatContainersList = flatContainersList.OrderBy(x => rng.Next()).ToList();

        // 4. Divide into 4 queues (matching your UI setup)
        int numQueues = 4;
        for (int i = 0; i < numQueues; i++) level.ResolutionQueues.Add(new List<GameLevelSchema.ContainerData>());
        
        for (int i = 0; i < flatContainersList.Count; i++)
        {
            level.ResolutionQueues[i % numQueues].Add(flatContainersList[i]);
        }

        // 5. Strict Sequential ID Assignment (The Final Fix)
        int strictIdCounter = 0;
        foreach (var queue in level.ResolutionQueues)
        {
            foreach (var container in queue)
            {
                container.Id = strictIdCounter++;
            }
        }
    }

    private void SaveLevelToJson(GameLevelSchema level, int index, string path)
    {
        string fileName = $"Level_{index:000}.json";
        string fullPath = Path.Combine(path, fileName);
        
        // FIXED: Do NOT ignore nulls (prints ContinuousPipe: null as expected)
        var settings = new JsonSerializerSettings { Formatting = Formatting.Indented };
        string json = JsonConvert.SerializeObject(level, settings);
        File.WriteAllText(fullPath, json);
    }
}