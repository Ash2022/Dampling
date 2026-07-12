using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public class DamplingSimulationAgentGreedy
{
    public class LevelAnalysisReport
    {
        public int TotalRunsSimulated { get; set; }
        public int SuccessfulWins { get; set; }
        public int FatalLosses { get; set; }
        public float WinRatePercentage => TotalRunsSimulated > 0 ? ((float)SuccessfulWins / TotalRunsSimulated) * 100f : 0f;

        public int MinMovesToWin { get; set; } = int.MaxValue;
        public int MaxMovesToWin { get; set; } = int.MinValue;
        public float AvgMovesToWin { get; set; }
        public float AvgMaxBeltDensityReached { get; set; }
        public long TotalExecutionTimeMs { get; set; }
        public bool TimeoutTriggered { get; set; }

        public Dictionary<string, int> FailStateDistribution { get; set; } = new Dictionary<string, int>();
        public List<string> SummaryLog { get; set; } = new List<string>();
    }

    private const int MaxMovesPerRunSafetyLimit = 1000; 
    private const long MaxBatchProcessingTimeMs = 4000; 

    public LevelAnalysisReport RunBatchSimulation(GameLevelSchema sourceLevelData, int iterationRunsCount)
    {
        LevelAnalysisReport report = new LevelAnalysisReport { TotalRunsSimulated = iterationRunsCount };
        Random randomProvider = new Random();
        Stopwatch batchTimer = Stopwatch.StartNew();

        int totalMovesAccumulator = 0;
        float totalMaxBeltDensityAccumulator = 0;

        for (int run = 0; run < iterationRunsCount; run++)
        {
            if (batchTimer.ElapsedMilliseconds > MaxBatchProcessingTimeMs)
            {
                report.TimeoutTriggered = true;
                report.TotalRunsSimulated = run; 
                report.SummaryLog.Add($"[TIMEOUT WARNING] Smart-Greedy simulation batch aborted early at {MaxBatchProcessingTimeMs}ms.");
                break;
            }

            DamplingGameCore dynamicEngineInstance = new DamplingGameCore();
            GameLevelSchema deepClonedSchema = DamplingGameUtils.CloneLevelSchema(sourceLevelData);
            dynamicEngineInstance.InitializeLevel(deepClonedSchema);

            int moveCount = 0;
            int peakBeltSizeInThisRun = 0;

            while (!dynamicEngineInstance.IsGameOver)
            {
                if (moveCount > MaxMovesPerRunSafetyLimit)
                {
                    dynamicEngineInstance.ExecutePlayerClick(-999, -999); 
                    break;
                }

                List<GameLevelSchema.Coordinate> playableCoordinates = GetPlayableMoves(dynamicEngineInstance);
                if (playableCoordinates.Count == 0) break;

                // --- SMART-GREEDY COMBINED SELECTION ---
                GameLevelSchema.Coordinate selectedMove = SelectSmartGreedyMove(dynamicEngineInstance, playableCoordinates, randomProvider);
                
                dynamicEngineInstance.ExecutePlayerClick(selectedMove.X, selectedMove.Y);
                moveCount++;

                if (dynamicEngineInstance.VirtualBelt.Count > peakBeltSizeInThisRun)
                {
                    peakBeltSizeInThisRun = dynamicEngineInstance.VirtualBelt.Count;
                }
            }

            totalMaxBeltDensityAccumulator += peakBeltSizeInThisRun;

            if (dynamicEngineInstance.IsGameWon)
            {
                report.SuccessfulWins++;
                totalMovesAccumulator += moveCount;
                if (moveCount < report.MinMovesToWin) report.MinMovesToWin = moveCount;
                if (moveCount > report.MaxMovesToWin) report.MaxMovesToWin = moveCount;
            }
            else
            {
                report.FatalLosses++;
                var primaryChokeColorIndex = dynamicEngineInstance.VirtualBelt.GroupBy(d => d.ColorIndex)
                    .OrderByDescending(g => g.Count())
                    .Select(g => (int?)g.Key)
                    .FirstOrDefault();

                string failureReason = primaryChokeColorIndex.HasValue ? $"Color: {primaryChokeColorIndex.Value}" : "Deadlock/Overflow";
                string failureReasonKey = $"Belt Jammed (Majority {failureReason})";
                if (!report.FailStateDistribution.ContainsKey(failureReasonKey))
                {
                    report.FailStateDistribution[failureReasonKey] = 0;
                }
                report.FailStateDistribution[failureReasonKey]++;
            }
        }

        batchTimer.Stop();
        report.TotalExecutionTimeMs = batchTimer.ElapsedMilliseconds;

        if (report.SuccessfulWins > 0) report.AvgMovesToWin = (float)totalMovesAccumulator / report.SuccessfulWins;
        else { report.MinMovesToWin = 0; report.MaxMovesToWin = 0; }

        report.AvgMaxBeltDensityReached = totalMaxBeltDensityAccumulator / Math.Max(1, report.TotalRunsSimulated);

        GenerateTextSummary(report);
        return report;
    }

    private GameLevelSchema.Coordinate SelectSmartGreedyMove(DamplingGameCore engine, List<GameLevelSchema.Coordinate> playableMoves, Random rand)
    {
        Dictionary<int, List<GameLevelSchema.Coordinate>> scoredMoveGroups = new Dictionary<int, List<GameLevelSchema.Coordinate>>();

        // Track empty spaces left in currently active target containers
        Dictionary<int, int> activeContainerDemands = new Dictionary<int, int>();
        if (engine.ActiveLevelData.ResolutionQueues != null)
        {
            foreach (var queue in engine.ActiveLevelData.ResolutionQueues)
            {
                var targetContainer = queue.FirstOrDefault(c => c.FilledSlotsCount < c.Capacity);
                if (targetContainer != null)
                {
                    int spacesLeft = targetContainer.Capacity - targetContainer.FilledSlotsCount;
                    if (!activeContainerDemands.ContainsKey(targetContainer.ColorIndex))
                        activeContainerDemands[targetContainer.ColorIndex] = 0;
                    activeContainerDemands[targetContainer.ColorIndex] += spacesLeft;
                }
            }
        }

        foreach (var move in playableMoves)
        {
            var cellNode = engine.ActiveLevelData.Grid.Matrix.FirstOrDefault(n => n.Position.X == move.X && n.Position.Y == move.Y);
            if (cellNode == null || cellNode.OccupyingUnit == null) continue;

            var items = cellNode.OccupyingUnit.InteriorContents;
            if (items == null || items.Count == 0) continue;

            var colorGroups = items.GroupBy(i => i.ColorIndex);
            int immediateClearanceScore = 0;

            foreach (var group in colorGroups)
            {
                if (activeContainerDemands.ContainsKey(group.Key))
                {
                    // Greedy optimization: How many slots vanish immediately
                    immediateClearanceScore += Math.Min(group.Count(), activeContainerDemands[group.Key]);
                }
            }

            if (!scoredMoveGroups.ContainsKey(immediateClearanceScore))
            {
                scoredMoveGroups[immediateClearanceScore] = new List<GameLevelSchema.Coordinate>();
            }
            scoredMoveGroups[immediateClearanceScore].Add(move);
        }

        // 1. Filter out only the highest immediate "Kill Count" scoring options
        int highestScore = scoredMoveGroups.Keys.Max();
        var bestGreedyMovesList = scoredMoveGroups[highestScore];

        // 2. Tie-Breaker + Safety Pass: Interleave items in 3-ball batches to predict survival
        int currentBeltCount = engine.VirtualBelt.Count;
        int maxBeltCapacity = 28;
        
        var safeOptimizedMoves = new List<GameLevelSchema.Coordinate>();
        int lowestResultingBeltCount = int.MaxValue;

        foreach (var move in bestGreedyMovesList)
        {
            var cellNode = engine.ActiveLevelData.Grid.Matrix.First(n => n.Position.X == move.X && n.Position.Y == move.Y);
            var items = cellNode.OccupyingUnit.InteriorContents;

            // Group by color to run micro-sim checks
            var primaryGroup = items.GroupBy(i => i.ColorIndex).OrderByDescending(g => g.Count()).First();
            int dominantColor = primaryGroup.Key;

            // Clone active container demands to calculate real changes per sub-burst
            var virtualDemands = new Dictionary<int, int>(activeContainerDemands);

            // --- THE 3-BALL BATCH LOOK-AHEAD INTEGRATION ---
            int simulatedBeltSpace = currentBeltCount;
            bool overfilledBelt = false;
            int totalItemsInPayload = items.Count;

            // Emit items in waves of 3 until the unit is empty
            while (totalItemsInPayload > 0)
            {
                int currentBurstSize = Math.Min(3, totalItemsInPayload);
                totalItemsInPayload -= currentBurstSize;

                simulatedBeltSpace += currentBurstSize;

                // Process matching drops during the burst
                if (virtualDemands.ContainsKey(dominantColor) && virtualDemands[dominantColor] > 0)
                {
                    int itemsCleared = Math.Min(currentBurstSize, virtualDemands[dominantColor]);
                    virtualDemands[dominantColor] -= itemsCleared;
                    simulatedBeltSpace -= itemsCleared; // Instantly flushed out
                }

                if (simulatedBeltSpace > maxBeltCapacity)
                {
                    overfilledBelt = true;
                }
            }

            // Rank moves based on who survived the 3-ball interleaved threshold safely
            if (!overfilledBelt)
            {
                if (simulatedBeltSpace < lowestResultingBeltCount)
                {
                    lowestResultingBeltCount = simulatedBeltSpace;
                    safeOptimizedMoves.Clear();
                    safeOptimizedMoves.Add(move);
                }
                else if (simulatedBeltSpace == lowestResultingBeltCount)
                {
                    safeOptimizedMoves.Add(move);
                }
            }
        }

        // Return the move that satisfies both constraints perfectly
        if (safeOptimizedMoves.Count > 0) return safeOptimizedMoves[rand.Next(safeOptimizedMoves.Count)];
        return bestGreedyMovesList[rand.Next(bestGreedyMovesList.Count)]; // Fallback
    }

    private List<GameLevelSchema.Coordinate> GetPlayableMoves(DamplingGameCore engine)
    {
        List<GameLevelSchema.Coordinate> activeOptionsPool = new List<GameLevelSchema.Coordinate>();
        HashSet<int> alreadyEvaluatedClusterIds = new HashSet<int>();

        foreach (var cellNode in engine.ActiveLevelData.Grid.Matrix)
        {
            if (cellNode.OccupyingUnit == null || engine.PlayedUnitIds.Contains(cellNode.OccupyingUnit.UnitId))
                continue;

            if (alreadyEvaluatedClusterIds.Contains(cellNode.OccupyingUnit.UnitId))
                continue;

            HashSet<int> clusterIds = engine.GetFullClusterIds(cellNode.OccupyingUnit.UnitId);
            foreach (var id in clusterIds) alreadyEvaluatedClusterIds.Add(id);

            if (!engine.IsUnitClusterBlocked(cellNode.Position, cellNode.OccupyingUnit, clusterIds))
            {
                activeOptionsPool.Add(cellNode.Position);
            }
        }
        return activeOptionsPool;
    }

    public void GenerateTextSummary(LevelAnalysisReport report)
    {
        report.SummaryLog.Insert(0, "==================================================");
        report.SummaryLog.Insert(1, $"     DAMPLING SMART-GREEDY AGENT V3 REPORT        ");
        report.SummaryLog.Insert(2, "==================================================");
        report.SummaryLog.Add($"Total Test Cycles Executed : {report.TotalRunsSimulated}");
        report.SummaryLog.Add($"Successful Wins            : {report.SuccessfulWins}");
        report.SummaryLog.Add($"Fatal Losses               : {report.FatalLosses}");
        report.SummaryLog.Add($"Calculated Win Rate        : {report.WinRatePercentage:F2}%");
        report.SummaryLog.Add($"Total Compute Duration     : {report.TotalExecutionTimeMs} ms");
        report.SummaryLog.Add("--------------------------------------------------");
        report.SummaryLog.Add($"Min Moves Required to Win  : {report.MinMovesToWin}");
        report.SummaryLog.Add($"Max Moves Required to Win  : {report.MaxMovesToWin}");
        report.SummaryLog.Add($"Average Moves to Clear Map : {report.AvgMovesToWin:F1}");
        report.SummaryLog.Add($"Avg Peak Belt Occupancy    : {report.AvgMaxBeltDensityReached:F1} items stacked");

        if (report.FailStateDistribution.Count > 0)
        {
            report.SummaryLog.Add("--------------------------------------------------");
            report.SummaryLog.Add("Failure Mode Distribution Tracking Log:");
            foreach (var kvp in report.FailStateDistribution)
            {
                float failPercentage = report.FatalLosses > 0 ? ((float)kvp.Value / report.FatalLosses) * 100f : 0f;
                report.SummaryLog.Add($" * {kvp.Key}: {kvp.Value} times ({failPercentage:F1}%)");
            }
        }
        report.SummaryLog.Add("==================================================");
    }
}