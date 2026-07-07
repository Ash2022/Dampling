using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public class DamplingSimulationAgentSmart
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
    private const long MaxBatchProcessingTimeMs = 5000; // Increased to 5s to allow for deeper heuristic calculations

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
                report.SummaryLog.Add($"[TIMEOUT WARNING] Smart simulation batch aborted early at {MaxBatchProcessingTimeMs}ms.");
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
                    report.SummaryLog.Add($"Run #{run + 1}: Terminated early—exceeded safety cap.");
                    break;
                }

                List<GameLevelSchema.Coordinate> playableCoordinates = GetPlayableMoves(dynamicEngineInstance);

                if (playableCoordinates.Count == 0) break;

                // --- SMART STRATEGIC MOVE SELECTION ---
                GameLevelSchema.Coordinate selectedMove = SelectSmartMove(dynamicEngineInstance, playableCoordinates, randomProvider);
                
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
                string primaryChokeColor = dynamicEngineInstance.VirtualBelt.GroupBy(d => d.ColorId)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .FirstOrDefault() ?? "Deadlock/Overflow";

                string failureReasonKey = $"Belt Jammed (Majority Color: {primaryChokeColor})";
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

    private GameLevelSchema.Coordinate SelectSmartMove(DamplingGameCore engine, List<GameLevelSchema.Coordinate> playableMoves, Random rand)
    {
        // Group available moves by their calculated heuristic priority tier
        var highPriorityMoves = new List<GameLevelSchema.Coordinate>();
        var normalPriorityMoves = new List<GameLevelSchema.Coordinate>();
        var riskyMoves = new List<GameLevelSchema.Coordinate>();

        int currentBeltCount = engine.VirtualBelt.Count;
        int maxBeltCapacity = 28; // Standard buffer ceiling

        // Fetch targets currently waiting at the front of resolution queues
        var activeTargetColors = new HashSet<string>();
        if (engine.ActiveLevelData.ResolutionQueues != null)
        {
            foreach (var queue in engine.ActiveLevelData.ResolutionQueues)
            {
                var firstActiveContainer = queue.FirstOrDefault(c => c.FilledSlotsCount < c.Capacity);
                if (firstActiveContainer != null)
                {
                    activeTargetColors.Add(firstActiveContainer.ColorId);
                }
            }
        }

        foreach (var move in playableMoves)
        {
            var cellNode = engine.ActiveLevelData.Grid.Matrix.FirstOrDefault(n => n.Position.X == move.X && n.Position.Y == move.Y);
            if (cellNode == null || cellNode.OccupyingUnit == null) continue;

            var items = cellNode.OccupyingUnit.InteriorContents;
            if (items == null || items.Count == 0) continue;

            // Group contents by color to look for high concentrations
            var primaryColorGroup = items.GroupBy(i => i.ColorId).OrderByDescending(g => g.Count()).First();
            string dominantColor = primaryColorGroup.Key;

            // --- THE VIRTUAL 3-BALL BATCH LOOK-AHEAD ---
            // Simulate how the belt handles items dynamically in sets of 3
            int simulatedBeltSpace = currentBeltCount;
            bool causesImmediateOverflow = false;
            int itemsClearingInstantly = 0;

            // Split the 9 total items into 3 batches of 3
            for (int batch = 0; batch < 3; batch++)
            {
                simulatedBeltSpace += 3;
                
                // If this batch matches a current target, simulate it clearing out instantly
                if (activeTargetColors.Contains(dominantColor))
                {
                    itemsClearingInstantly += 3;
                    simulatedBeltSpace -= 3; // Freed up
                }

                if (simulatedBeltSpace > maxBeltCapacity)
                {
                    causesImmediateOverflow = true;
                }
            }

            // Assign weights based on the dynamic look-ahead result
            if (itemsClearingInstantly >= 6 && !causesImmediateOverflow)
            {
                // Tier 1: High-Efficiency Clearing Move (Clears queues without overflow)
                highPriorityMoves.Add(move);
            }
            else if (causesImmediateOverflow)
            {
                // Tier 3: Risky Move (Likely to cause deadlocks or overflow bounds)
                riskyMoves.Add(move);
            }
            else
            {
                // Tier 2: Safe matching or sustaining setup move
                normalPriorityMoves.Add(move);
            }
        }

        // Return random item from the highest available successful tier
        if (highPriorityMoves.Count > 0) return highPriorityMoves[rand.Next(highPriorityMoves.Count)];
        if (normalPriorityMoves.Count > 0) return normalPriorityMoves[rand.Next(normalPriorityMoves.Count)];
        if (riskyMoves.Count > 0) return riskyMoves[rand.Next(riskyMoves.Count)];

        return playableMoves[rand.Next(playableMoves.Count)]; // Fallback
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
        report.SummaryLog.Insert(1, $"        DAMPLING SMART AGENT V2 REPORT            ");
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