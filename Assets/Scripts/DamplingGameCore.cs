using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

public class DamplingGameCore
{
    // --- Runtime Core States ---
    public GameLevelSchema ActiveLevelData { get; set; }
    private Dictionary<GameLevelSchema.Coordinate, GameLevelSchema.CellNode> gridMatrix;
    public List<GameLevelSchema.DumplingItem> VirtualBelt { get; private set; }
    public List<List<GameLevelSchema.ContainerData>> DynamicQueues { get; private set; }
    public HashSet<int> PlayedUnitIds { get; private set; }

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
        public int TargetId { get; set; }        
        public string ColorValue { get; set; }     
        public int QueueIndex { get; set; }       
        public object Payload { get; set; }       
    }

    // --- Core Engine Initializer ---
    public void InitializeLevel(GameLevelSchema levelData)
    {
        ActiveLevelData = levelData;
        VirtualBelt = new List<GameLevelSchema.DumplingItem>();
        PlayedUnitIds = new HashSet<int>();
        IsGameOver = false;
        IsGameWon = false;

        gridMatrix = new Dictionary<GameLevelSchema.Coordinate, GameLevelSchema.CellNode>();
        foreach (var node in ActiveLevelData.Grid.Matrix) {
            gridMatrix[node.Position] = node;
        }

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

        var coord = new GameLevelSchema.Coordinate(x, y);
        if (!gridMatrix.TryGetValue(coord, out var primaryCellNode)) return outputTransactionHistory;
        
        var activeUnit = primaryCellNode.OccupyingUnit;
        if (activeUnit == null || PlayedUnitIds.Contains(activeUnit.UnitId)) return outputTransactionHistory;

        // Collect all related linked units into an evaluation block for atomic simulation validation
        List<GameLevelSchema.GridUnit> linkedCluster = new List<GameLevelSchema.GridUnit>();
        HashSet<int> visitedClusterIds = new HashSet<int>();
        Queue<GameLevelSchema.GridUnit> clusterQueue = new Queue<GameLevelSchema.GridUnit>();

        clusterQueue.Enqueue(activeUnit);
        visitedClusterIds.Add(activeUnit.UnitId);

        while (clusterQueue.Count > 0)
        {
            var currentClusterUnit = clusterQueue.Dequeue();
            linkedCluster.Add(currentClusterUnit);

            foreach (var linkedId in currentClusterUnit.LinkedUnitIds)
            {
                if (!PlayedUnitIds.Contains(linkedId) && !visitedClusterIds.Contains(linkedId))
                {
                    var foundLinkedUnit = FindUnitById(linkedId);
                    if (foundLinkedUnit != null)
                    {
                        visitedClusterIds.Add(linkedId);
                        clusterQueue.Enqueue(foundLinkedUnit);
                    }
                }
            }
        }

        // ATOMIC TRANSACTION RULE: Verify blockers across the ENTIRE linked collection
        // If even one unit inside this linked chain is blocked, the play interaction is invalid
        foreach (var clusterUnit in linkedCluster)
        {
            var unitCellNode = FindCellNodeByUnitId(clusterUnit.UnitId);
            if (IsUnitBlocked(unitCellNode.Position, clusterUnit))
            {
                return outputTransactionHistory; // Atomic block rejection
            }
        }

        // Execute processing steps for the verified playable transaction group
        foreach (var currentUnit in linkedCluster)
        {
            PlayedUnitIds.Add(currentUnit.UnitId);

            foreach (var dumpling in currentUnit.InteriorContents)
            {
                VirtualBelt.Add(dumpling);
            }
        }

        ProcessBeltResolutionPipeline(outputTransactionHistory);
        EvaluateNewlyUnblockedUnits(outputTransactionHistory);
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

            for (int i = 0; i < VirtualBelt.Count; i++)
            {
                var dumpling = VirtualBelt[i];
                
                for (int q = 0; q < DynamicQueues.Count; q++)
                {
                    if (DynamicQueues[q].Count == 0) continue;

                    var activeContainer = DynamicQueues[q][0];
                    if (activeContainer.ColorId == dumpling.ColorId && activeContainer.Capacity > 0)
                    {
                        activeContainer.Capacity--;
                        VirtualBelt.RemoveAt(i);
                        
                        transactions.Add(new EngineEvent
                        {
                            EventType = EngineEventType.DumplingMovedToContainer,
                            TargetId = activeContainer.Id,
                            ColorValue = dumpling.ColorId,
                            QueueIndex = q
                        });

                        if (activeContainer.Capacity <= 0)
                        {
                            transactions.Add(new EngineEvent
                            {
                                EventType = EngineEventType.ContainerResolved,
                                TargetId = activeContainer.Id,
                                QueueIndex = q
                            });
                            
                            DynamicQueues[q].RemoveAt(0); 
                        }

                        stateChanged = true;
                        i--; 
                        break; 
                    }
                }
                if (stateChanged) break; 
            }
        } while (stateChanged);
    }

    private void EvaluateNewlyUnblockedUnits(List<EngineEvent> transactions)
    {
        foreach (var cellNode in ActiveLevelData.Grid.Matrix)
        {
            if (cellNode.OccupyingUnit == null || PlayedUnitIds.Contains(cellNode.OccupyingUnit.UnitId)) continue;

            if (!IsUnitBlocked(cellNode.Position, cellNode.OccupyingUnit))
            {
                transactions.Add(new EngineEvent
                {
                    EventType = EngineEventType.UnitUnblocked,
                    TargetId = cellNode.OccupyingUnit.UnitId,
                    Payload = new Vector2Int(cellNode.Position.X, cellNode.Position.Y)
                });
            }
        }
    }

    private void EvaluateGameStatusStates(List<EngineEvent> transactions)
    {
        bool allQueuesEmpty = DynamicQueues.All(q => q.Count == 0);
        if (allQueuesEmpty)
        {
            IsGameOver = true;
            IsGameWon = true;
            transactions.Add(new EngineEvent { EventType = EngineEventType.LevelWon });
            return;
        }

        if (VirtualBelt.Count >= ActiveLevelData.ConveyorBeltMaxCapacity)
        {
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
        // 1. Explicit Blocker and Key validation checkpoint
        foreach (var dependencyId in unit.ExplicitlyBlockedByUnitIds)
        {
            if (!PlayedUnitIds.Contains(dependencyId))
            {
                return true; 
            }
        }

        // 2. Spatial 2D Flow Pathfinding matrix evaluation routing
        if (coord.Y == 0)
        {
            return false; 
        }

        Queue<GameLevelSchema.Coordinate> queue = new Queue<GameLevelSchema.Coordinate>();
        HashSet<GameLevelSchema.Coordinate> visited = new HashSet<GameLevelSchema.Coordinate>();

        queue.Enqueue(coord);
        visited.Add(coord);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            var neighbors = new GameLevelSchema.Coordinate[]
            {
                new GameLevelSchema.Coordinate(current.X, current.Y - 1), 
                new GameLevelSchema.Coordinate(current.X - 1, current.Y), 
                new GameLevelSchema.Coordinate(current.X + 1, current.Y)  
            };

            foreach (var neighborCoord in neighbors)
            {
                if (visited.Contains(neighborCoord)) continue;

                if (gridMatrix.TryGetValue(neighborCoord, out var neighborCell) && 
                    neighborCell.IsPlayablePath && (neighborCell.OccupyingUnit == null || PlayedUnitIds.Contains(neighborCell.OccupyingUnit.UnitId)))
                {
                    if (neighborCoord.Y == 0) return false; 

                    visited.Add(neighborCoord);
                    queue.Enqueue(neighborCoord);
                }
            }
        }

        return true; 
    }

    private GameLevelSchema.GridUnit FindUnitById(int id)
    {
        foreach (var node in gridMatrix.Values)
        {
            if (node.OccupyingUnit != null && node.OccupyingUnit.UnitId == id) return node.OccupyingUnit;
        }
        return null;
    }

    private GameLevelSchema.CellNode FindCellNodeByUnitId(int id)
    {
        foreach (var node in gridMatrix.Values)
        {
            if (node.OccupyingUnit != null && node.OccupyingUnit.UnitId == id) return node;
        }
        return null;
    }
}