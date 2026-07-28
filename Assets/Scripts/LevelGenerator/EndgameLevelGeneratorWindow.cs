using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

public class EndgameLevelGeneratorWindow : EditorWindow
{
    private int levelsToGenerate = 5;
    private float minTargetWinRate = 0.005f;
    private float maxTargetWinRate = 0.25f;
    private float minColorsFloat = 5f;
    private float maxColorsFloat = 8f;
    
    private int maxGridCols = 7;
    private int maxGridRows = 6;
    private int maxAttempts = 100;

    // Feature Odds Sliders
    private float oddsIce = 0.10f;
    private float oddsHidden = 0.05f;
    private float oddsLink = 0.10f;
    private float oddsLock = 0.05f;
    private float oddsKey = 0.05f;

    private string outputFolderPath = "Assets/Resources/EndgameLevels";
    private bool isProcessing = false;
    private int currentLevelIndex = 1;
    private int currentAttempt = 0;
    private const int DUMPLINGS_PER_UNIT = 9;

    private DamplingSimulationAgent botAgent;
    private System.Random rng;
    private readonly int[] MasterPalette = { 0, 1, 2, 3, 4, 5, 6, 7 };

    [MenuItem("Tools/Endgame Level Generator")]
    public static void ShowWindow() => GetWindow<EndgameLevelGeneratorWindow>("Endgame Generator");

    private void OnEnable() => EditorApplication.update += OnUpdateTick;
    private void OnDisable() { EditorApplication.update -= OnUpdateTick; isProcessing = false; }

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();

        GUILayout.Label("Endgame Level Parameters", EditorStyles.boldLabel);
        
        levelsToGenerate = EditorGUILayout.IntSlider("Levels to Generate", levelsToGenerate, 5, 500);
        maxAttempts = EditorGUILayout.IntSlider("Max Attempts Per Level", maxAttempts, 1, 1000);
        
        EditorGUILayout.LabelField($"Win Rate Range ({minTargetWinRate:P1} - {maxTargetWinRate:P1})");
        EditorGUILayout.MinMaxSlider(ref minTargetWinRate, ref maxTargetWinRate, 0.005f, 0.99f);
        
        EditorGUILayout.LabelField($"Color Count Range ({(int)minColorsFloat} - {(int)maxColorsFloat})");
        EditorGUILayout.MinMaxSlider(ref minColorsFloat, ref maxColorsFloat, 5f, 8f);

        GUILayout.Space(10);
        GUILayout.Label("Mechanic Odds", EditorStyles.boldLabel);
        oddsIce = EditorGUILayout.Slider("Ice Odds", oddsIce, 0f, 1f);
        oddsHidden = EditorGUILayout.Slider("Hidden Odds", oddsHidden, 0f, 1f);
        oddsLink = EditorGUILayout.Slider("Link Odds", oddsLink, 0f, 1f);
        oddsLock = EditorGUILayout.Slider("Lock Odds", oddsLock, 0f, 1f);
        oddsKey = EditorGUILayout.Slider("Key Odds", oddsKey, 0f, 1f);
        GUILayout.Space(10);

        outputFolderPath = EditorGUILayout.TextField("Output Path", outputFolderPath);

        if (!isProcessing && GUILayout.Button("Start Generation")) StartBatch();
        if (isProcessing && GUILayout.Button("Stop Generation")) 
        {
            isProcessing = false;
            EditorUtility.ClearProgressBar();
        }

        if (EditorGUI.EndChangeCheck()) Repaint();
    }

    private void StartBatch()
    {
        Directory.CreateDirectory(outputFolderPath);
        botAgent = new DamplingSimulationAgent();
        rng = new System.Random();
        currentLevelIndex = 1;
        currentAttempt = 0;
        isProcessing = true;
    }

    private void OnUpdateTick()
    {
        if (!isProcessing) return;

        float progress = (float)currentLevelIndex / levelsToGenerate;
        EditorUtility.DisplayProgressBar("Generating Endgame", $"Level {currentLevelIndex} - Attempt {currentAttempt}/{maxAttempts}", progress);

        currentAttempt++;
        GameLevelSchema candidate = GenerateCandidate();
        
        var report = botAgent.RunBatchSimulation(candidate, 100);
        float actualWinRate = report.WinRatePercentage / 100f;

        if ((actualWinRate >= minTargetWinRate && actualWinRate <= maxTargetWinRate) || currentAttempt >= maxAttempts)
        {
            SaveLevel(candidate, currentLevelIndex, actualWinRate);
            currentLevelIndex++;
            currentAttempt = 0;
        }

        if (currentLevelIndex > levelsToGenerate)
        {
            isProcessing = false;
            EditorUtility.ClearProgressBar();
        }
    }

    private GameLevelSchema GenerateCandidate()
    {
        int cols = rng.Next(5, maxGridCols + 1);
        int rows = rng.Next(3, maxGridRows + 1);
        int minC = Mathf.RoundToInt(minColorsFloat);
        int maxC = Mathf.RoundToInt(maxColorsFloat);
        if (minC > maxC) minC = maxC; 
        
        int colorCount = rng.Next(minC, maxC + 1);
        List<int> activeColors = MasterPalette.Take(colorCount).ToList();

        GameLevelSchema level = new GameLevelSchema
        {
            LevelId = currentLevelIndex,
            LevelName = $"Endgame_{currentLevelIndex}",
            ConveyorBeltMaxCapacity = 30,
            Grid = new GameLevelSchema.GridTopology { Columns = cols, Rows = rows, Matrix = new List<GameLevelSchema.CellNode>() },
            ResolutionQueues = new List<List<GameLevelSchema.ContainerData>>()
        };

        int unitIdCounter = 0;
        Dictionary<int, int> colorDistribution = activeColors.ToDictionary(c => c, c => 0);
        HashSet<Vector2Int> pipeExits = new HashSet<Vector2Int>();

        int pipeCount = rng.Next(1, 3);
        List<Vector2Int> pipeLocations = new List<Vector2Int>();
        for (int i = 0; i < pipeCount; i++)
        {
            int px = rng.Next(1, cols - 1);
            int py = rng.Next(1, rows); 
            Vector2Int pos = new Vector2Int(px, py);
            pipeLocations.Add(pos);
            pipeExits.Add(new Vector2Int(px, py - 1)); 
        }

        List<KeyValuePair<Vector2Int, GameLevelSchema.GridUnit>> availableUnitsForLinks = new List<KeyValuePair<Vector2Int, GameLevelSchema.GridUnit>>();
        List<GameLevelSchema.GridUnit> availableUnitsForLocks = new List<GameLevelSchema.GridUnit>();
        List<GameLevelSchema.GridUnit> availableUnitsForKeys = new List<GameLevelSchema.GridUnit>();

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                Vector2Int currentPos = new Vector2Int(x, y);
                var node = new GameLevelSchema.CellNode { Position = new GameLevelSchema.Coordinate(x, y), IsPlayablePath = true };

                // Enforce strict X-Axis middle placement (never on column 0 or cols - 1)
                if (x == 0 || x == cols - 1)
                {
                    node.IsPlayablePath = false;
                    node.OccupyingUnit = null;
                    node.ContinuousPipe = null;
                    level.Grid.Matrix.Add(node);
                    continue;
                }

                if (pipeLocations.Contains(currentPos))
                {
                    int emissions = rng.Next(3, 5);
                    node.ContinuousPipe = new GameLevelSchema.PipeGenerator { MaxTotalEmissions = emissions, ReservoirQueue = new List<GameLevelSchema.GridUnit>() };
                    
                    for (int e = 0; e < emissions; e++)
                    {
                        int c = activeColors[rng.Next(activeColors.Count)];
                        var pu = new GameLevelSchema.GridUnit { UnitId = unitIdCounter++, InteriorContents = new List<GameLevelSchema.DumplingItem>() };
                        for (int d = 0; d < DUMPLINGS_PER_UNIT; d++) pu.InteriorContents.Add(new GameLevelSchema.DumplingItem { ColorIndex = c });
                        node.ContinuousPipe.ReservoirQueue.Add(pu);
                        colorDistribution[c]++;
                    }
                }
                else
                {
                    int c = activeColors[rng.Next(activeColors.Count)];
                    var unit = new GameLevelSchema.GridUnit 
                    { 
                        UnitId = unitIdCounter++, 
                        IsHiddenUntilUnblocked = false,
                        IceLayers = 0,
                        KeyLockPairIndex = -1,
                        InteriorContents = new List<GameLevelSchema.DumplingItem>(),
                        ExplicitlyBlockedByUnitIds = new List<int>(),
                        LinkedUnitIds = new List<int>()
                    };

                    for (int d = 0; d < DUMPLINGS_PER_UNIT; d++) 
                    {
                        unit.InteriorContents.Add(new GameLevelSchema.DumplingItem { ColorIndex = c });
                    }
                    
                    node.OccupyingUnit = unit;
                    colorDistribution[c]++;

                    if (!pipeExits.Contains(currentPos))
                    {
                        if (rng.NextDouble() < oddsIce) { unit.IceLayers = rng.Next(1, 3); }
                        else if (rng.NextDouble() < oddsHidden) { unit.IsHiddenUntilUnblocked = true; }
                        else if (rng.NextDouble() < oddsLink) { availableUnitsForLinks.Add(new KeyValuePair<Vector2Int, GameLevelSchema.GridUnit>(currentPos, unit)); }
                        else if (rng.NextDouble() < oddsLock) { availableUnitsForLocks.Add(unit); }
                        else if (rng.NextDouble() < oddsKey) { availableUnitsForKeys.Add(unit); }
                    }
                }
                level.Grid.Matrix.Add(node);
            }
        }

        // Strict Link Adjacency Rules (Orthogonal or immediate diagonal difference of 1)
        var unlinked = availableUnitsForLinks.OrderBy(u => rng.Next()).ToList();
        HashSet<int> processedLinks = new HashSet<int>();

        for (int i = 0; i < unlinked.Count; i++)
        {
            if (processedLinks.Contains(unlinked[i].Value.UnitId)) continue;
            var unitA = unlinked[i];

            for (int j = i + 1; j < unlinked.Count; j++)
            {
                if (processedLinks.Contains(unlinked[j].Value.UnitId)) continue;
                var unitB = unlinked[j];
                
                int dx = Mathf.Abs(unitA.Key.x - unitB.Key.x);
                int dy = Mathf.Abs(unitA.Key.y - unitB.Key.y);

                if (dx <= 1 && dy <= 1 && (dx + dy > 0))
                {
                    unitA.Value.LinkedUnitIds.Add(unitB.Value.UnitId);
                    unitB.Value.LinkedUnitIds.Add(unitA.Value.UnitId);
                    processedLinks.Add(unitA.Value.UnitId);
                    processedLinks.Add(unitB.Value.UnitId);
                    break;
                }
            }
        }

        // Locks and Keys (Hard capped to max 3 pairs)
        availableUnitsForLocks = availableUnitsForLocks.OrderBy(x => rng.Next()).ToList();
        availableUnitsForKeys = availableUnitsForKeys.OrderBy(x => rng.Next()).ToList();
        int lockKeyPairs = Mathf.Min(3, Mathf.Min(availableUnitsForLocks.Count, availableUnitsForKeys.Count));
        
        for (int i = 0; i < lockKeyPairs; i++)
        {
            int pairId = i + 1;
            availableUnitsForLocks[i].ExplicitlyBlockedByUnitIds.Add(availableUnitsForKeys[i].UnitId);
            availableUnitsForLocks[i].KeyLockPairIndex = pairId;
            availableUnitsForKeys[i].KeyLockPairIndex = pairId;
        }

        // Resolution Queues with full schema compliance
        List<GameLevelSchema.ContainerData> flatContainers = new List<GameLevelSchema.ContainerData>();
        int containerIdCounter = 0;
        foreach (var kvp in colorDistribution)
        {
            int remainingDumplings = kvp.Value * DUMPLINGS_PER_UNIT;
            int infiniteLoopGuard = 1000;
            
            while (remainingDumplings > 0 && infiniteLoopGuard-- > 0)
            {
                int cap = Mathf.Min(remainingDumplings, 3);
                flatContainers.Add(new GameLevelSchema.ContainerData 
                { 
                    Id = containerIdCounter++,
                    ColorIndex = kvp.Key, 
                    Capacity = cap, 
                    FilledSlotsCount = 0,
                    startHidden = false
                });
                remainingDumplings -= cap;
            }
        }

        flatContainers = flatContainers.OrderBy(x => rng.Next()).ToList();
        for (int i = 0; i < 4; i++) level.ResolutionQueues.Add(new List<GameLevelSchema.ContainerData>());
        for (int i = 0; i < flatContainers.Count; i++)
        {
            level.ResolutionQueues[i % 4].Add(flatContainers[i]);
        }

        return level;
    }

    private void SaveLevel(GameLevelSchema level, int index, float winRate)
    {
        int wrInt = Mathf.RoundToInt(winRate * 100);
        string file = $"Endgame_{index:000}_WR_{wrInt}.json";
        string json = JsonConvert.SerializeObject(level, new JsonSerializerSettings { Formatting = Formatting.Indented });
        File.WriteAllText(Path.Combine(outputFolderPath, file), json);
    }
}