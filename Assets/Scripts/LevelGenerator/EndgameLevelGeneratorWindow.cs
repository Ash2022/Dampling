using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using static GameLevelSchema;

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

    private float oddsIce = 0.10f;
    private float oddsHiddenUnit = 0.05f;
    private float oddsLink = 0.10f;
    private float oddsLockKeyPair = 0.10f;
    private float oddsHiddenContainer = 0.10f;

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
        oddsHiddenUnit = EditorGUILayout.Slider("Hidden Unit Odds", oddsHiddenUnit, 0f, 1f);
        oddsLink = EditorGUILayout.Slider("Link Odds", oddsLink, 0f, 1f);
        oddsLockKeyPair = EditorGUILayout.Slider("Lock/Key Pair Odds", oddsLockKeyPair, 0f, 1f);
        oddsHiddenContainer = EditorGUILayout.Slider("Hidden Container Odds", oddsHiddenContainer, 0f, 1f);
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
            AttachMetaData(candidate, actualWinRate);
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
        int cols = maxGridCols;
        int rows = maxGridRows;
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

        HashSet<Vector2Int> playableCells = GenerateAsymmetricalShape(cols, rows);

        int unitIdCounter = 0;
        Dictionary<int, int> colorDistribution = activeColors.ToDictionary(c => c, c => 0);
        HashSet<Vector2Int> pipeExits = new HashSet<Vector2Int>();
        List<Vector2Int> pipeLocations = new List<Vector2Int>();

        int pipeCount = rng.Next(1, 3);

        var validPipeCandidates = playableCells.Where(p => p.y > 0).OrderBy(p => rng.Next()).ToList();
        HashSet<int> usedPipeColumns = new HashSet<int>();

        for (int i = 0; i < pipeCount && validPipeCandidates.Count > 0; i++)
        {
            var chosenPos = validPipeCandidates.First();
            validPipeCandidates.Remove(chosenPos);

            if (usedPipeColumns.Contains(chosenPos.x)) continue;

            usedPipeColumns.Add(chosenPos.x);
            pipeLocations.Add(chosenPos);
            pipeExits.Add(new Vector2Int(chosenPos.x, chosenPos.y - 1));
        }

        List<KeyValuePair<Vector2Int, GameLevelSchema.GridUnit>> availableUnitsForLinks = new List<KeyValuePair<Vector2Int, GameLevelSchema.GridUnit>>();
        List<KeyValuePair<Vector2Int, GameLevelSchema.GridUnit>> lockKeyCandidates = new List<KeyValuePair<Vector2Int, GameLevelSchema.GridUnit>>();

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                Vector2Int currentPos = new Vector2Int(x, y);
                var node = new GameLevelSchema.CellNode { Position = new GameLevelSchema.Coordinate(x, y), IsPlayablePath = true };

                if (!playableCells.Contains(currentPos))
                {
                    node.IsPlayablePath = false;
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
                        int adjacentStandardUnits = CountAdjacentStandardUnits(currentPos, playableCells, pipeLocations);

                        if (rng.NextDouble() < oddsIce && adjacentStandardUnits >= 1)
                        {
                            unit.IceLayers = rng.Next(1, Mathf.Min(4, adjacentStandardUnits + 1));
                        }
                        else if (y > 0 && rng.NextDouble() < oddsHiddenUnit)
                        {
                            unit.IsHiddenUntilUnblocked = true;
                        }
                        else if (rng.NextDouble() < oddsLink)
                        {
                            availableUnitsForLinks.Add(new KeyValuePair<Vector2Int, GameLevelSchema.GridUnit>(currentPos, unit));
                        }
                        else if (rng.NextDouble() < oddsLockKeyPair)
                        {
                            lockKeyCandidates.Add(new KeyValuePair<Vector2Int, GameLevelSchema.GridUnit>(currentPos, unit));
                        }
                    }
                }
                level.Grid.Matrix.Add(node);
            }
        }

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

        var pairedCandidates = lockKeyCandidates.OrderBy(c => rng.Next()).ToList();
        int lockKeyPairs = Mathf.Min(3, pairedCandidates.Count / 2);

        for (int i = 0; i < lockKeyPairs; i++)
        {
            var item1 = pairedCandidates[i * 2];
            var item2 = pairedCandidates[i * 2 + 1];

            if (item1.Key.y == item2.Key.y) continue;

            var lockNode = item1.Key.y < item2.Key.y ? item1 : item2;
            var keyNode = item1.Key.y > item2.Key.y ? item1 : item2;

            int pairId = i + 1;
            lockNode.Value.ExplicitlyBlockedByUnitIds.Add(keyNode.Value.UnitId);
            lockNode.Value.KeyLockPairIndex = pairId;
            keyNode.Value.KeyLockPairIndex = pairId;
        }

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
                    startHidden = rng.NextDouble() < oddsHiddenContainer
                });
                remainingDumplings -= cap;
            }
        }

        flatContainers = flatContainers.OrderBy(x => rng.Next()).ToList();
        for (int i = 0; i < 4; i++) level.ResolutionQueues.Add(new List<GameLevelSchema.ContainerData>());

        for (int i = 0; i < flatContainers.Count; i++)
        {
            var targetQueue = level.ResolutionQueues[i % 4];

            if (targetQueue.Count == 0)
                flatContainers[i].startHidden = false;

            targetQueue.Add(flatContainers[i]);
        }

        return level;
    }

    private HashSet<Vector2Int> GenerateAsymmetricalShape(int cols, int rows)
    {
        HashSet<Vector2Int> shape = new HashSet<Vector2Int>();

        for (int x = 1; x < cols - 1; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                shape.Add(new Vector2Int(x, y));
            }
        }

        int cuts = rng.Next(2, 5);
        for (int i = 0; i < cuts; i++)
        {
            int w = rng.Next(1, 3);
            int h = rng.Next(1, 3);

            int cx = rng.NextDouble() > 0.5f ? 1 : cols - 1 - w;
            int cy = rng.NextDouble() > 0.5f ? 0 : rows - h;

            for (int x = cx; x < cx + w; x++)
            {
                for (int y = cy; y < cy + h; y++)
                {
                    shape.Remove(new Vector2Int(x, y));
                }
            }
        }

        if (shape.Count < 12) return GenerateAsymmetricalShape(cols, rows);

        return shape;
    }

    private int CountAdjacentStandardUnits(Vector2Int pos, HashSet<Vector2Int> playable, List<Vector2Int> pipes)
    {
        int count = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                Vector2Int neighbor = new Vector2Int(pos.x + dx, pos.y + dy);
                if (playable.Contains(neighbor) && !pipes.Contains(neighbor)) count++;
            }
        }
        return count;
    }

    private void SaveLevel(GameLevelSchema level, int index, float winRate)
    {
        int wrInt = Mathf.RoundToInt(winRate * 100);
        string file = $"Endgame_{index:000}_WR_{wrInt}.json";
        string json = JsonConvert.SerializeObject(level, new JsonSerializerSettings { Formatting = Formatting.Indented });
        File.WriteAllText(Path.Combine(outputFolderPath, file), json);
    }

    private void AttachMetaData(GameLevelSchema level, float finalWinRate)
    {
        int totalUnits = 0, hiddenUnits = 0, iceUnits = 0, pipeCount = 0, totalLinks = 0, hiddenContainers = 0;
        HashSet<int> countedKeyLocks = new HashSet<int>();

        foreach (var node in level.Grid.Matrix)
        {
            if (node.ContinuousPipe != null)
            {
                pipeCount++;
                totalUnits += (int)node.ContinuousPipe.MaxTotalEmissions;
            }
            else if (node.OccupyingUnit != null)
            {
                totalUnits++;
                if (node.OccupyingUnit.IsHiddenUntilUnblocked) hiddenUnits++;
                if (node.OccupyingUnit.IceLayers > 0) iceUnits++;

                totalLinks += node.OccupyingUnit.LinkedUnitIds.Count;

                if (node.OccupyingUnit.KeyLockPairIndex > 0)
                    countedKeyLocks.Add(node.OccupyingUnit.KeyLockPairIndex);
            }
        }

        foreach (var queue in level.ResolutionQueues)
        {
            foreach (var container in queue)
            {
                if (container.startHidden) hiddenContainers++;
            }
        }

        level.MetaData = new LevelMetaData
        {
            GridColumns = level.Grid.Columns,
            GridRows = level.Grid.Rows,
            TotalUnits = totalUnits,
            HiddenUnitCount = hiddenUnits,
            PipeCount = pipeCount,
            KeyLockCount = countedKeyLocks.Count,
            LinkCount = totalLinks / 2, // Divided by 2 because links are bidirectional in the schema
            HiddenContainerCount = hiddenContainers,
            IceCount = iceUnits,
            WinRate = finalWinRate
        };
    }
}