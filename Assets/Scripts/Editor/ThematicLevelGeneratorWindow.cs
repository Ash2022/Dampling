using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using static GameLevelSchema;

public class ThematicLevelGeneratorWindow : EditorWindow
{
    private int levelsToGenerate = 5;
    private float minTargetWinRate = 0.005f;
    private float maxTargetWinRate = 0.25f;
    private float minColorsFloat = 6f;
    private float maxColorsFloat = 8f;

    private int maxGridCols = 7;
    private int maxGridRows = 6;
    private int maxAttempts = 100;

    private Vector2Int pipeQuota = new Vector2Int(1, 4);
    private Vector2Int iceQuota = new Vector2Int(2, 8);
    private Vector2Int hiddenUnitQuota = new Vector2Int(2, 8);
    private Vector2Int linkQuota = new Vector2Int(1, 3);
    private Vector2Int lockKeyQuota = new Vector2Int(1, 3);
    private Vector2Int hiddenContainerQuota = new Vector2Int(0, 20);
    private Vector2Int coverMapQuota = new Vector2Int(1, 1);

    private string outputFolderPath = "Assets/Resources/ThematicLevels";
    private bool isProcessing = false;
    private int currentLevelIndex = 1;
    private int currentAttempt = 0;
    private const int DUMPLINGS_PER_UNIT = 9;

    private DamplingSimulationAgent botAgent;
    private System.Random rng;
    private readonly int[] MasterPalette = { 0, 1, 2, 3, 4, 5, 6, 7 };

    private enum FeatureType { Pipe, Ice, Hidden, Link, LockKey, CoverMap }

    [MenuItem("Tools/Thematic Level Generator")]
    public static void ShowWindow() => GetWindow<ThematicLevelGeneratorWindow>("Thematic Generator");

    private void OnEnable() => EditorApplication.update += OnUpdateTick;
    private void OnDisable() { EditorApplication.update -= OnUpdateTick; isProcessing = false; }

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();

        GUILayout.Label("Thematic Level Parameters", EditorStyles.boldLabel);

        levelsToGenerate = EditorGUILayout.IntSlider("Levels to Generate", levelsToGenerate, 5, 500);
        maxAttempts = EditorGUILayout.IntSlider("Max Attempts Per Level", maxAttempts, 1, 1000);

        EditorGUILayout.LabelField($"Win Rate Range ({minTargetWinRate:P1} - {maxTargetWinRate:P1})");
        EditorGUILayout.MinMaxSlider(ref minTargetWinRate, ref maxTargetWinRate, 0.005f, 0.99f);

        EditorGUILayout.LabelField($"Color Count Range ({(int)minColorsFloat} - {(int)maxColorsFloat})");
        EditorGUILayout.MinMaxSlider(ref minColorsFloat, ref maxColorsFloat, 6f, 8f);

        GUILayout.Space(10);
        GUILayout.Label("Feature Quotas (Min/Max)", EditorStyles.boldLabel);
        pipeQuota = EditorGUILayout.Vector2IntField("Pipes", pipeQuota);
        iceQuota = EditorGUILayout.Vector2IntField("Ice Units", iceQuota);
        hiddenUnitQuota = EditorGUILayout.Vector2IntField("Hidden Units", hiddenUnitQuota);
        linkQuota = EditorGUILayout.Vector2IntField("Link Pairs", linkQuota);
        lockKeyQuota = EditorGUILayout.Vector2IntField("Lock/Key Pairs", lockKeyQuota);
        hiddenContainerQuota = EditorGUILayout.Vector2IntField("Hidden Containers", hiddenContainerQuota);
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
        EditorUtility.DisplayProgressBar("Generating Thematic", $"Level {currentLevelIndex} - Attempt {currentAttempt}/{maxAttempts}", progress);

        currentAttempt++;
        GameLevelSchema candidate = GenerateCandidate();

        var report = botAgent.RunBatchSimulation(candidate, 100);
        float actualWinRate = report.WinRatePercentage / 100f;

        if (actualWinRate >= minTargetWinRate && actualWinRate <= maxTargetWinRate)
        {
            AttachMetaData(candidate, actualWinRate);
            SaveLevel(candidate, currentLevelIndex, actualWinRate);
            currentLevelIndex++;
            currentAttempt = 0;
        }
        else if (currentAttempt >= maxAttempts)
        {
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
            LevelName = $"Thematic_{currentLevelIndex}",
            ConveyorBeltMaxCapacity = 30,
            Grid = new GameLevelSchema.GridTopology { Columns = cols, Rows = rows, Matrix = new List<GameLevelSchema.CellNode>() },
            ResolutionQueues = new List<List<GameLevelSchema.ContainerData>>()
        };

        HashSet<Vector2Int> playableCells = rng.NextDouble() >= 0.5f ? GenerateSymmetricalShape(cols, rows) : GenerateAsymmetricalShape(cols, rows);
        int unitIdCounter = 0;
        Dictionary<int, int> colorDistribution = activeColors.ToDictionary(c => c, c => 0);

        List<FeatureType> activeFeatures = DetermineArchetype();

        int targetPipes = activeFeatures.Contains(FeatureType.Pipe) ? rng.Next(pipeQuota.x, pipeQuota.y + 1) : 0;
        int targetIce = activeFeatures.Contains(FeatureType.Ice) ? rng.Next(iceQuota.x, iceQuota.y + 1) : 0;
        int targetHidden = activeFeatures.Contains(FeatureType.Hidden) ? rng.Next(hiddenUnitQuota.x, hiddenUnitQuota.y + 1) : 0;
        int targetLinks = activeFeatures.Contains(FeatureType.Link) ? rng.Next(linkQuota.x, linkQuota.y + 1) : 0;
        int targetLocks = activeFeatures.Contains(FeatureType.LockKey) ? rng.Next(lockKeyQuota.x, lockKeyQuota.y + 1) : 0;

        HashSet<Vector2Int> pipeExits = new HashSet<Vector2Int>();
        List<Vector2Int> pipeLocations = new List<Vector2Int>();

        var validPipeCandidates = playableCells.Where(p => p.y > 0).OrderBy(p => rng.Next()).ToList();
        HashSet<int> usedPipeColumns = new HashSet<int>();

        for (int i = 0; i < targetPipes && validPipeCandidates.Count > 0; i++)
        {
            var chosenPos = validPipeCandidates.First();
            validPipeCandidates.Remove(chosenPos);

            if (usedPipeColumns.Contains(chosenPos.x)) continue;

            usedPipeColumns.Add(chosenPos.x);
            pipeLocations.Add(chosenPos);
            pipeExits.Add(new Vector2Int(chosenPos.x, chosenPos.y - 1));
        }

        List<KeyValuePair<Vector2Int, GameLevelSchema.GridUnit>> standardUnits = new List<KeyValuePair<Vector2Int, GameLevelSchema.GridUnit>>();

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
                        standardUnits.Add(new KeyValuePair<Vector2Int, GameLevelSchema.GridUnit>(currentPos, unit));
                    }
                }
                level.Grid.Matrix.Add(node);
            }
        }

        standardUnits = standardUnits.OrderBy(u => rng.Next()).ToList();

        int linkPairsCreated = 0;
        for (int i = 0; i < standardUnits.Count && linkPairsCreated < targetLinks; i++)
        {
            var unitA = standardUnits[i];
            if (unitA.Value.LinkedUnitIds.Count > 0) continue;

            for (int j = i + 1; j < standardUnits.Count; j++)
            {
                var unitB = standardUnits[j];
                if (unitB.Value.LinkedUnitIds.Count > 0) continue;

                int dx = Mathf.Abs(unitA.Key.x - unitB.Key.x);
                int dy = Mathf.Abs(unitA.Key.y - unitB.Key.y);

                if (dx <= 1 && dy <= 1 && (dx + dy > 0))
                {
                    unitA.Value.LinkedUnitIds.Add(unitB.Value.UnitId);
                    unitB.Value.LinkedUnitIds.Add(unitA.Value.UnitId);
                    linkPairsCreated++;
                    break;
                }
            }
        }

        int locksCreated = 0;
        List<KeyValuePair<Vector2Int, GameLevelSchema.GridUnit>> lockKeyCandidates = new List<KeyValuePair<Vector2Int, GameLevelSchema.GridUnit>>();

        for (int i = 0; i < standardUnits.Count; i++)
        {
            if (standardUnits[i].Value.KeyLockPairIndex == -1 && standardUnits[i].Value.LinkedUnitIds.Count == 0)
            {
                lockKeyCandidates.Add(standardUnits[i]);
            }
        }

        var pairedCandidates = lockKeyCandidates.OrderBy(c => rng.Next()).ToList();
        int lockKeyPairs = Mathf.Min(targetLocks, pairedCandidates.Count / 2);

        for (int i = 0; i < lockKeyPairs; i++)
        {
            var item1 = pairedCandidates[i * 2];
            var item2 = pairedCandidates[i * 2 + 1];

            if (item1.Key.y == item2.Key.y) continue;

            var lockNode = item1.Key.y < item2.Key.y ? item1 : item2;
            var keyNode = item1.Key.y > item2.Key.y ? item1 : item2;

            int pairId = locksCreated + 1;

            lockNode.Value.KeyLockPairIndex = pairId;
            lockNode.Value.ExplicitlyBlockedByUnitIds.Clear();
            lockNode.Value.ExplicitlyBlockedByUnitIds.Add(keyNode.Value.UnitId);

            keyNode.Value.KeyLockPairIndex = pairId;
            keyNode.Value.ExplicitlyBlockedByUnitIds.Clear();

            locksCreated++;
        }

        int hiddenCreated = 0;
        for (int i = 0; i < standardUnits.Count && hiddenCreated < targetHidden; i++)
        {
            var unitKVP = standardUnits[i];
            if (unitKVP.Key.y > 0 && unitKVP.Value.KeyLockPairIndex == -1 && unitKVP.Value.LinkedUnitIds.Count == 0 && !unitKVP.Value.IsHiddenUntilUnblocked)
            {
                unitKVP.Value.IsHiddenUntilUnblocked = true;
                hiddenCreated++;
            }
        }

        int iceCreated = 0;
        for (int i = 0; i < standardUnits.Count && iceCreated < targetIce; i++)
        {
            var unitKVP = standardUnits[i];
            if (unitKVP.Key.y > 0 && unitKVP.Value.IceLayers == 0 && unitKVP.Value.KeyLockPairIndex == -1 && unitKVP.Value.LinkedUnitIds.Count == 0 && !unitKVP.Value.IsHiddenUntilUnblocked)
            {
                int adjacentStandardUnits = CountAdjacentStandardUnits(unitKVP.Key, playableCells, pipeLocations);
                if (adjacentStandardUnits >= 1)
                {
                    unitKVP.Value.IceLayers = rng.Next(1, Mathf.Min(4, adjacentStandardUnits + 1));
                    iceCreated++;
                }
            }
        }

        int[] validSizes = new int[] { 1, 2, 4 };
        int targetCoverMapUnits = validSizes[rng.Next(validSizes.Length)];

        List<Vector2Int> availableMapCandidates = standardUnits
            .Where(u => u.Key.y > 0 && u.Value.IceLayers == 0 && u.Value.KeyLockPairIndex == -1 && u.Value.LinkedUnitIds.Count == 0 && !u.Value.IsHiddenUntilUnblocked)
            .Select(u => u.Key)
            .OrderBy(c => rng.Next())
            .ToList();

        List<Vector2Int> selectedMapCoords = new List<Vector2Int>();

        foreach (var startCoord in availableMapCandidates)
        {
            if (targetCoverMapUnits == 1)
            {
                selectedMapCoords.Add(startCoord);
                break;
            }

            if (targetCoverMapUnits == 2)
            {
                Vector2Int[] potentialNeighbors = {
            new Vector2Int(startCoord.x + 1, startCoord.y),
            new Vector2Int(startCoord.x - 1, startCoord.y),
            new Vector2Int(startCoord.x, startCoord.y + 1),
            new Vector2Int(startCoord.x, startCoord.y - 1)
        };

                var validNeighbor = potentialNeighbors.FirstOrDefault(n => availableMapCandidates.Contains(n));
                if (validNeighbor != default)
                {
                    selectedMapCoords.Add(startCoord);
                    selectedMapCoords.Add(validNeighbor);
                    break;
                }
            }

            if (targetCoverMapUnits == 4)
            {
                Vector2Int right = new Vector2Int(startCoord.x + 1, startCoord.y);
                Vector2Int up = new Vector2Int(startCoord.x, startCoord.y + 1);
                Vector2Int diag = new Vector2Int(startCoord.x + 1, startCoord.y + 1);

                if (availableMapCandidates.Contains(right) && availableMapCandidates.Contains(up) && availableMapCandidates.Contains(diag))
                {
                    selectedMapCoords.Add(startCoord);
                    selectedMapCoords.Add(right);
                    selectedMapCoords.Add(up);
                    selectedMapCoords.Add(diag);
                    break;
                }
            }
        }

        int totalUnitsCount = level.Grid.Matrix.Count(n => n.IsPlayablePath);
        int counterValue = rng.Next(Mathf.Max(1, Mathf.RoundToInt(totalUnitsCount * 0.1f)), Mathf.RoundToInt(totalUnitsCount * 0.5f) + 1);

        if (selectedMapCoords.Count > 0)
        {
            level.CoverMap = new GameLevelSchema.CoverMapData
            {
                Counter = counterValue,

                CoveredUnitIds = selectedMapCoords.Select(c =>
               {
                   var matchingUnit = level.Grid.Matrix.First(m => m.Position.X == c.x && m.Position.Y == c.y);
                   return matchingUnit.OccupyingUnit.UnitId;
               }).ToList()
            };
        }

        List<GameLevelSchema.ContainerData> flatContainers = new List<GameLevelSchema.ContainerData>();
        int containerIdCounter = 0;
        int targetHiddenContainers = rng.Next(hiddenContainerQuota.x, hiddenContainerQuota.y + 1);
        int hiddenContainersCreated = 0;

        foreach (var kvp in colorDistribution)
        {
            int remainingDumplings = kvp.Value * DUMPLINGS_PER_UNIT;
            int infiniteLoopGuard = 1000;

            while (remainingDumplings > 0 && infiniteLoopGuard-- > 0)
            {
                int cap = Mathf.Min(remainingDumplings, 3);
                bool makeHidden = hiddenContainersCreated < targetHiddenContainers;
                if (makeHidden) hiddenContainersCreated++;

                flatContainers.Add(new GameLevelSchema.ContainerData
                {
                    Id = containerIdCounter++,
                    ColorIndex = kvp.Key,
                    Capacity = cap,
                    FilledSlotsCount = 0,
                    startHidden = makeHidden
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
            {
                flatContainers[i].startHidden = false;
            }

            targetQueue.Add(flatContainers[i]);
        }

        return level;
    }

    private List<FeatureType> DetermineArchetype()
    {
        int roll = rng.Next(0, 100);
        int numFeatures = 1;

        if (roll < 10) numFeatures = 5;
        else if (roll < 20) numFeatures = 4;
        else if (roll < 50) numFeatures = 3;
        else if (roll < 80) numFeatures = 2;

        List<FeatureType> pool = new List<FeatureType> { FeatureType.Pipe, FeatureType.Ice, FeatureType.Hidden, FeatureType.Link, FeatureType.LockKey, FeatureType.CoverMap };
        return pool.OrderBy(x => rng.Next()).Take(numFeatures).ToList();
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

    private HashSet<Vector2Int> GenerateSymmetricalShape(int cols, int rows)
    {
        HashSet<Vector2Int> shape = new HashSet<Vector2Int>();

        for (int x = 1; x < cols - 1; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                shape.Add(new Vector2Int(x, y));
            }
        }

        int midX = cols / 2;
        int cuts = rng.Next(1, 4);

        for (int i = 0; i < cuts; i++)
        {
            int cx = rng.Next(1, midX);
            int w = rng.Next(1, midX - cx + 1);
            int h = rng.Next(1, 3);
            int cy = rng.NextDouble() > 0.5f ? 0 : rows - h;

            for (int x = cx; x < cx + w; x++)
            {
                for (int y = cy; y < cy + h; y++)
                {
                    shape.Remove(new Vector2Int(x, y));
                    shape.Remove(new Vector2Int(cols - 1 - x, y));
                }
            }
        }

        if (cols % 2 != 0 && rng.NextDouble() >= 0.5f)
        {
            int h = rng.Next(1, 3);
            int cy = rng.NextDouble() > 0.5f ? 0 : rows - h;

            for (int y = cy; y < cy + h; y++)
            {
                shape.Remove(new Vector2Int(midX, y));
            }
        }

        if (shape.Count < 12) return GenerateSymmetricalShape(cols, rows);

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
        string file = $"Thematic_{index:000}_WR_{wrInt}.json";
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
            MapCount = level.CoverMap != null ? level.CoverMap.CoveredUnitIds.Count : 0,
            WinRate = finalWinRate
        };
    }
}