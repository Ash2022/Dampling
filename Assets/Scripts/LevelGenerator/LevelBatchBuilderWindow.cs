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

    // --- Core Systems ---
    private DamplingSimulationAgent botAgent;
    private System.Random rng;
    private readonly string[] MasterColorPalette = { "Red", "Blue", "Green", "Yellow", "Purple", "Orange", "Pink", "Cyan", "Brown", "Teal" };

    [MenuItem("Tools/Level Batch Builder")]
    public static void ShowWindow()
    {
        GetWindow<LevelBatchBuilderWindow>("Level Builder");
    }

    private void OnEnable()
    {
        EditorApplication.update += OnUpdateTick;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnUpdateTick;
        if (isProcessing) CancelProcessing();
    }

    // --- THE UI ---
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
            if (GUILayout.Button("Generate Batch", GUILayout.Height(40)))
            {
                StartProcessing();
            }
        }
        else
        {
            GUI.enabled = true; 
            if (GUILayout.Button("CANCEL GENERATION", GUILayout.Height(40)))
            {
                CancelProcessing();
            }
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

        if (!Directory.Exists(outputFolderPath))
            Directory.CreateDirectory(outputFolderPath);

        botAgent = new DamplingSimulationAgent();
        rng = new System.Random();

        currentProcessingLevel = startLevel;
        currentAttempt = 0;
        isProcessing = true;
    }

    private void CancelProcessing()
    {
        isProcessing = false;
        EditorUtility.ClearProgressBar();
        Debug.LogWarning("Level Generation Canceled by User.");
    }

    private void OnUpdateTick()
    {
        if (!isProcessing) return;

        float totalLevelsToProcess = (endLevel - startLevel) + 1;
        float levelsProcessed = (currentProcessingLevel - startLevel);
        float progress = levelsProcessed / totalLevelsToProcess;
        float attemptFraction = ((float)currentAttempt / MAX_ATTEMPTS_PER_LEVEL) * (1f / totalLevelsToProcess);
        
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
        var report = botAgent.RunBatchSimulation(candidate, 200);

        float minAcceptable = rules.TargetWinRate - rules.WinRateTolerance;
        float maxAcceptable = rules.TargetWinRate + rules.WinRateTolerance;
        float actualWinRate = report.WinRatePercentage / 100f;

        if (actualWinRate >= minAcceptable && actualWinRate <= maxAcceptable)
        {
            Debug.Log($"<color=cyan>Level {currentProcessingLevel}</color> baked in {currentAttempt} attempts. [Target: {rules.TargetWinRate:P0} | Actual: {actualWinRate:P0}]");
            SaveLevelToJson(candidate, currentProcessingLevel, outputFolderPath);
            
            currentProcessingLevel++;
            currentAttempt = 0;
            return;
        }

        if (currentAttempt >= MAX_ATTEMPTS_PER_LEVEL)
        {
            Debug.LogWarning($"<color=yellow>Level {currentProcessingLevel}</color> failed to hit difficulty target after {MAX_ATTEMPTS_PER_LEVEL} attempts. Saving best effort. [Target: {rules.TargetWinRate:P0} | Actual: {actualWinRate:P0}]");
            SaveLevelToJson(candidate, currentProcessingLevel, outputFolderPath);
            
            currentProcessingLevel++;
            currentAttempt = 0;
            return;
        }
    }

    // --- COMPILE FIXES APPLIED HERE ---
    private GameLevelSchema GenerateCandidateLevel(LevelGeneratorConfig.LevelRuleset rules, int levelIndex)
    {
        int width = rng.Next(rules.MinGridSize, rules.MaxGridSize + 1);
        int height = rng.Next(rules.MinGridSize, rules.MaxGridSize + 1);

        GameLevelSchema level = new GameLevelSchema
        {
            LevelId = levelIndex,
            ConveyorBeltMaxCapacity = 7,
            // FIX 3: Use GridTopology and initialize rows/columns
            Grid = new GameLevelSchema.GridTopology 
            { 
                Columns = width,
                Rows = height,
                Matrix = new List<GameLevelSchema.CellNode>() 
            },
            ResolutionQueues = new List<List<GameLevelSchema.ContainerData>>()
        };

        List<string> activeColors = MasterColorPalette.Take(rules.MaxColors).ToList();
        Dictionary<string, int> totalColorCounts = new Dictionary<string, int>();
        foreach (var c in activeColors) totalColorCounts[c] = 0;

        int globalUnitIdCounter = 1;

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

                if (rules.AllowedMechanics.Contains(UnlockableFeature.BlockedCell) && rng.NextDouble() < 0.1f)
                {
                    node.IsPlayablePath = false;
                }
                else
                {
                    string randomColor = activeColors[rng.Next(activeColors.Count)];
                    
                    // FIX 2: Populate DumplingItem inside InteriorContents instead of ColorId directly
                    node.OccupyingUnit = new GameLevelSchema.GridUnit
                    {
                        UnitId = globalUnitIdCounter++,
                        IceLayers = 0,
                        InteriorContents = new List<GameLevelSchema.DumplingItem>
                        {
                            new GameLevelSchema.DumplingItem { ColorId = randomColor }
                        }
                    };
                    totalColorCounts[randomColor]++;
                }
                level.Grid.Matrix.Add(node);
            }
        }

        // Passed globalUnitIdCounter by ref so pipes can consume IDs
        ApplyMechanics(level, rules, activeColors, totalColorCounts, width, height, ref globalUnitIdCounter);
        GenerateResolutionQueues(level, totalColorCounts);

        return level;
    }

    private void ApplyMechanics(GameLevelSchema level, LevelGeneratorConfig.LevelRuleset rules, List<string> activeColors, Dictionary<string, int> colorTracker, int width, int height, ref int unitIdCounter)
    {
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
                    
                    // FIX 1: Use PipeGenerator and populate the ReservoirQueue with actual GridUnits
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
                            InteriorContents = new List<GameLevelSchema.DumplingItem>
                            {
                                new GameLevelSchema.DumplingItem { ColorId = randomColor }
                            }
                        };
                        
                        pipeNode.ContinuousPipe.ReservoirQueue.Add(queuedUnit);
                        colorTracker[randomColor]++; 
                    }
                }
            }
        }

        if (rules.AllowedMechanics.Contains(UnlockableFeature.IceLayers))
        {
            var activeUnits = level.Grid.Matrix.Where(n => n.OccupyingUnit != null).ToList();
            if (activeUnits.Count > 0)
            {
                int numIced = rng.Next(1, activeUnits.Count / 3 + 1); 
                for (int i = 0; i < numIced; i++)
                {
                    activeUnits[rng.Next(activeUnits.Count)].OccupyingUnit.IceLayers = rng.Next(1, 3);
                }
            }
        }

        if (rules.AllowedMechanics.Contains(UnlockableFeature.Crates))
        {
            var emptyNodes = level.Grid.Matrix.Where(n => n.IsPlayablePath && n.OccupyingUnit == null && n.ContinuousPipe == null).ToList();
            int numCrates = rng.Next(1, 4);
            for (int i = 0; i < Math.Min(numCrates, emptyNodes.Count); i++)
            {
                emptyNodes[i].CrateDurability = rng.Next(1, 3);
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

        if (rules.AllowedMechanics.Contains(UnlockableFeature.LocksAndKeys))
        {
            var units = level.Grid.Matrix.Where(n => n.OccupyingUnit != null).Select(n => n.OccupyingUnit).ToList();
            if (units.Count >= 2)
            {
                var lockUnit = units[0];
                var keyUnit = units[1];
                if (lockUnit.UnitId != keyUnit.UnitId)
                {
                    lockUnit.ExplicitlyBlockedByUnitIds.Add(keyUnit.UnitId);
                }
            }
        }
    }

    private void GenerateResolutionQueues(GameLevelSchema level, Dictionary<string, int> colorTracker)
    {
        int containerIdCounter = 1;
        var queue = new List<GameLevelSchema.ContainerData>();

        foreach (var kvp in colorTracker)
        {
            string color = kvp.Key;
            int amount = kvp.Value;

            while (amount > 0)
            {
                int capacity = Math.Min(amount, 3);
                queue.Add(new GameLevelSchema.ContainerData
                {
                    Id = containerIdCounter++,
                    ColorId = color,
                    Capacity = capacity
                });
                amount -= capacity;
            }
        }

        queue = queue.OrderBy(x => rng.Next()).ToList();
        level.ResolutionQueues.Add(queue);
    }

    private void SaveLevelToJson(GameLevelSchema level, int index, string path)
    {
        string fileName = $"Level_{index:000}.json";
        string fullPath = Path.Combine(path, fileName);
        var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.Indented };
        string json = JsonConvert.SerializeObject(level, settings);
        File.WriteAllText(fullPath, json);
    }
}