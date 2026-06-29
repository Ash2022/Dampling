using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class DamplingGameCore
{
    // --- Runtime Core States ---
    public GameLevelSchema ActiveLevelData { get; private set; }
    public List<GameLevelSchema.DumplingItem> VirtualBelt { get; private set; }
    public List<List<GameLevelSchema.ContainerData>> DynamicQueues { get; private set; }
    public HashSet<Guid> PlayedUnitIds { get; private set; }

    public bool IsGameOver { get; private set; }
    public bool IsGameWon { get; private set; }

    // --- Structural Feedback Transaction Packets ---
    public enum EngineEventType
    {
        DumplingMovedToContainer,
        ContainerResolved,
        UnitUnblocked,
        LevelWon,
        LevelLost
    }

    public class EngineEvent
    {
        public EngineEventType EventType { get; set; }
        public Guid TargetId { get; set; }        // Unit ID, Container ID, etc.
        public string ColorValue { get; set; }     // Transferred element color
        public int QueueIndex { get; set; }       // Target column lane indices
        public object Payload { get; set; }        // Extensible debugging telemetry
    }

    // --- Core Engine Initializer ---
    public void InitializeLevel(GameLevelSchema levelData)
    {
        ActiveLevelData = levelData;
        VirtualBelt = new List<GameLevelSchema.DumplingItem>();
        PlayedUnitIds = new HashSet<Guid>();
        IsGameOver = false;
        IsGameWon = false;

        // Instantiate dynamic deep-cloned tracking copies of the demand resolution layout structures
        DynamicQueues = new List<List<GameLevelSchema.ContainerData>>();
        foreach (var originalQueue in levelData.ResolutionQueues)
        {
            var clonedQueue = new List<GameLevelSchema.ContainerData>();
            foreach (var container in originalQueue)
            {
                clonedQueue.Add(new GameLevelSchema.ContainerData
                {
                    Id = container.Id,
                    ColorId = container.ColorId,
                    Capacity = container.Capacity
                });
            }
            DynamicQueues.Add(clonedQueue);
        }
    }

    // --- Main Click Processing Loop Intermediary ---
    public List<EngineEvent> ExecutePlayerClick(int x, int y)
    {
        List<EngineEvent> outputTransactionHistory = new List<EngineEvent>();

        if (IsGameOver) return outputTransactionHistory;

        // 1. Position Verification & Key Lookup
        var primaryCellNode = ActiveLevelData.Grid.Matrix.FirstOrDefault(c => c.Position.X == x && c.Position.Y == y);
        if (primaryCellNode == null)
        {
            return outputTransactionHistory; // Invalid location targeted or cell doesn't exist
        }

        var activeUnit = primaryCellNode.OccupyingUnit;
        if (activeUnit == null || PlayedUnitIds.Contains(activeUnit.Id))
        {
            return outputTransactionHistory; // Node empty or already spent
        }

        // 2. Dynamic Topological Path Blockage Graph Calculations
        if (IsUnitBlocked(primaryCellNode.Position, activeUnit))
        {
            return outputTransactionHistory; // Blocked node interaction rejected
        }

        // 3. Collect Evaluation Target Execution Ring (Including Linked Units)
        Queue<GameLevelSchema.GridUnit> executionQueue = new Queue<GameLevelSchema.GridUnit>();
        executionQueue.Enqueue(activeUnit);

        HashSet<Guid> visitedInThisClick = new HashSet<Guid> { activeUnit.Id };

        while (executionQueue.Count > 0)
        {
            var currentUnit = executionQueue.Dequeue();
            PlayedUnitIds.Add(currentUnit.Id);

            // Dump interior payload systematically onto the back array of the virtual belt line
            foreach (var dumpling in currentUnit.InteriorContents)
            {
                VirtualBelt.Add(dumpling);
            }

            // Route execution parameters down the linkage network
            foreach (var linkedId in currentUnit.LinkedUnitIds)
            {
                if (!PlayedUnitIds.Contains(linkedId) && !visitedInThisClick.Contains(linkedId))
                {
                    // Locate linked structural units on the target grid mapping arrays
                    var linkedUnit = FindUnitById(linkedId);
                    if (linkedUnit != null)
                    {
                        visitedInThisClick.Add(linkedId);
                        executionQueue.Enqueue(linkedUnit);
                    }
                }
            }
        }

        // 4. Run Core Matching Pipeline Mechanics Loop
        ProcessBeltResolutionPipeline(outputTransactionHistory);

        // 5. Check and Calculate Newly Unlocked System Parameters
        EvaluateNewlyUnblockedUnits(outputTransactionHistory);

        // 6. Final Boundary State Validations
        EvaluateGameStatusStates(outputTransactionHistory);

        return outputTransactionHistory;
    }

    // --- Automated Processing Logic Loops ---
    private void ProcessBeltResolutionPipeline(List<EngineEvent> transactions)
    {
        bool stateChanged;
        do
        {
            stateChanged = false;

            // Iterate through the belt. Since it maps physics behaviors, any slot matching the active lanes flies out
            for (int i = 0; i < VirtualBelt.Count; i++)
            {
                var dumpling = VirtualBelt[i];
                
                // Scan the front container of each active lane array layout matching structural metrics
                for (int q = 0; q < DynamicQueues.Count; q++)
                {
                    if (DynamicQueues[q].Count == 0) continue;

                    var activeContainer = DynamicQueues[q][0];
                    if (activeContainer.ColorId == dumpling.ColorId && activeContainer.Capacity > 0)
                    {
                        // Deduct volume capacity requirements sequentially
                        activeContainer.Capacity--;
                        VirtualBelt.RemoveAt(i);
                        
                        transactions.Add(new EngineEvent
                        {
                            EventType = EngineEventType.DumplingMovedToContainer,
                            TargetId = activeContainer.Id,
                            ColorValue = dumpling.ColorId,
                            QueueIndex = q
                        });

                        // Evaluate zero-sum completion metrics for individual container segments
                        if (activeContainer.Capacity <= 0)
                        {
                            transactions.Add(new EngineEvent
                            {
                                EventType = EngineEventType.ContainerResolved,
                                TargetId = activeContainer.Id,
                                QueueIndex = q
                            });
                            
                            DynamicQueues[q].RemoveAt(0); // Shift lane line index layout items up by 1 position
                        }

                        stateChanged = true;
                        i--; // Counteract sequential indexing offset changes natively
                        break; 
                    }
                }
                if (stateChanged) break; // Break loop execution layer up to process from head position again
            }
        } while (stateChanged);
    }

    private void EvaluateNewlyUnblockedUnits(List<EngineEvent> transactions)
    {
        foreach (var cellNode in ActiveLevelData.Grid.Matrix)
        {
            if (cellNode.OccupyingUnit == null || PlayedUnitIds.Contains(cellNode.OccupyingUnit.Id)) continue;

            // Check if this unplayed unit is now unblocked
            if (!IsUnitBlocked(cellNode.Position, cellNode.OccupyingUnit))
            {
                transactions.Add(new EngineEvent
                {
                    EventType = EngineEventType.UnitUnblocked,
                    TargetId = cellNode.OccupyingUnit.Id,
                    Payload = new Vector2Int(cellNode.Position.X, cellNode.Position.Y)
                });
            }
        }
    }

    private void EvaluateGameStatusStates(List<EngineEvent> transactions)
    {
        // WIN CHECK: Are all demand-side queues completely resolved and dried out?
        bool allQueuesEmpty = DynamicQueues.All(q => q.Count == 0);
        if (allQueuesEmpty)
        {
            IsGameOver = true;
            IsGameWon = true;
            transactions.Add(new EngineEvent { EventType = EngineEventType.LevelWon });
            return;
        }

        // LOSE CHECK: Has the conveyor belt completely overloaded past the maximum boundary limits?
        if (VirtualBelt.Count >= ActiveLevelData.ConveyorBeltMaxCapacity)
        {
            // Verify if a stalemate condition exists where no items on the belt match the current front targets
            bool matchPossible = false;
            foreach (var dumpling in VirtualBelt)
            {
                for (int q = 0; q < DynamicQueues.Count; q++)
                {
                    if (DynamicQueues[q].Count == 0) continue;
                    if (DynamicQueues[q][0].ColorId == dumpling.ColorId)
                    {
                        matchPossible = true;
                        break;
                    }
                }
                if (matchPossible) break;
            }

            if (!matchPossible)
            {
                IsGameOver = true;
                IsGameWon = false;
                transactions.Add(new EngineEvent { EventType = EngineEventType.LevelLost });
            }
        }
    }

    // --- Dependency Calculations Helper Methods ---
    public bool IsUnitBlocked(GameLevelSchema.Coordinate coord, GameLevelSchema.GridUnit unit)
    {
        // 1. Trivial structural spatial layout verification rule: y=0 row is natively unblocked by geography
        if (coord.Y > 0)
        {
            // Check if there is an active unit blocking path directly above it at Y - 1
            var cellAboveKey = new GameLevelSchema.Coordinate(coord.X, coord.Y - 1);
            var cellAbove = ActiveLevelData.Grid.Matrix.FirstOrDefault(c => c.Position.X == cellAboveKey.X && c.Position.Y == cellAboveKey.Y);
            if (cellAbove != null) {
                if (cellAbove.OccupyingUnit != null && !PlayedUnitIds.Contains(cellAbove.OccupyingUnit.Id))
                {
                    return true; // Simple positional line track is blocked
                }
            }
        }

        // 2. Explicit dependency validation checking loop
        foreach (var dependencyId in unit.ExplicitlyBlockedByUnitIds)
        {
            // If the explicit blocker item isn't in our cleared set, the block continues operating
            if (!PlayedUnitIds.Contains(dependencyId))
            {
                return true; 
            }
        }

        return false; // Free node path verified
    }

    private GameLevelSchema.GridUnit FindUnitById(Guid id)
    {
        return ActiveLevelData.Grid.Matrix
            .Select(node => node.OccupyingUnit)
            .FirstOrDefault(unit => unit != null && unit.Id == id);
    }
}