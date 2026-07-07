using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public class DamplingSimulationAgent
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

    private const int MaxMovesPerRunSafetyLimit = 1000; // Hard cap per game run to break infinite loops
    private const long MaxBatchProcessingTimeMs = 3000;  // 3-second maximum total cutoff time for the whole batch

    public LevelAnalysisReport RunBatchSimulation(GameLevelSchema sourceLevelData, int iterationRunsCount)
    {
        LevelAnalysisReport report = new LevelAnalysisReport { TotalRunsSimulated = iterationRunsCount };
        Random randomProvider = new Random();
        Stopwatch batchTimer = Stopwatch.StartNew();

        int totalMovesAccumulator = 0;
        float totalMaxBeltDensityAccumulator = 0;

        for (int run = 0; run < iterationRunsCount; run++)
        {
            // Time Out Safety Check: Stop everything if the total run is taking too long
            if (batchTimer.ElapsedMilliseconds > MaxBatchProcessingTimeMs)
            {
                report.TimeoutTriggered = true;
                report.TotalRunsSimulated = run; // Update with the actual completed run count
                report.SummaryLog.Add($"[TIMEOUT WARNING] Simulation batch aborted early at {MaxBatchProcessingTimeMs}ms to prevent editor freezing.");
                break;
            }

            DamplingGameCore dynamicEngineInstance = new DamplingGameCore();

            // Safe deep clone
            GameLevelSchema deepClonedSchema = DamplingGameUtils.CloneLevelSchema(sourceLevelData);
            dynamicEngineInstance.InitializeLevel(deepClonedSchema);

            int moveCount = 0;
            int peakBeltSizeInThisRun = 0;

            while (!dynamicEngineInstance.IsGameOver)
            {
                // Turn Limit Safety Check: Stop individual run if it gets stuck in an infinite matching loop
                if (moveCount > MaxMovesPerRunSafetyLimit)
                {
                    dynamicEngineInstance.ExecutePlayerClick(-999, -999); // Force artificial fail state termination internally
                    report.SummaryLog.Add($"Run #{run + 1}: Terminated early—exceeded single-game turn threshold safety cap.");
                    break;
                }

                List<GameLevelSchema.Coordinate> playableCoordinates = GetPlayableMoves(dynamicEngineInstance);

                if (playableCoordinates.Count == 0)
                {
                    break; // Stalemate deadlock
                }

                var selectedMove = playableCoordinates[randomProvider.Next(playableCoordinates.Count)];
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

        if (report.SuccessfulWins > 0)
        {
            report.AvgMovesToWin = (float)totalMovesAccumulator / report.SuccessfulWins;
        }
        else
        {
            report.MinMovesToWin = 0;
            report.MaxMovesToWin = 0;
        }

        report.AvgMaxBeltDensityReached = totalMaxBeltDensityAccumulator / Math.Max(1, report.TotalRunsSimulated);

        GenerateTextSummary(report);
        return report;
    }

    private List<GameLevelSchema.Coordinate> GetPlayableMoves(DamplingGameCore engine)
    {
        List<GameLevelSchema.Coordinate> activeOptionsPool = new List<GameLevelSchema.Coordinate>();

        // We use this to prevent the bot from adding multiple coordinates of the SAME linked cluster 
        // to the options pool, which would skew the random selection weight.
        HashSet<int> alreadyEvaluatedClusterIds = new HashSet<int>();

        foreach (var cellNode in engine.ActiveLevelData.Grid.Matrix)
        {
            // Skip empty cells or units that have already been played
            if (cellNode.OccupyingUnit == null || engine.PlayedUnitIds.Contains(cellNode.OccupyingUnit.UnitId))
                continue;

            // Skip if we already evaluated a different piece of this same linked cluster
            if (alreadyEvaluatedClusterIds.Contains(cellNode.OccupyingUnit.UnitId))
                continue;

            // 1. GATHER THE CLUSTER (The Fix)
            // Fetch the full list of IDs tied to this unit's chain
            HashSet<int> clusterIds = engine.GetFullClusterIds(cellNode.OccupyingUnit.UnitId);

            // Mark all units in this cluster as evaluated so we don't process them again in this loop
            foreach (var id in clusterIds)
            {
                alreadyEvaluatedClusterIds.Add(id);
            }

            // 2. CHECK THE CLUSTER
            // Pass the clusterIds into the pathfinder so it knows it can walk through linked partners
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
        report.SummaryLog.Insert(1, $"        DAMPLING LEVEL ANALYSIS REPORT          ");
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