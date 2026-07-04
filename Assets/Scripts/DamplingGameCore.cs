using System;
using System.Collections.Generic;
using System.Linq;
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

    // --- Optional View Notification Callbacks ---
    private Action<int> OnUnitUnblocked;                  // Param: UnitId
    private Action<int, int> OnUnitIceChanged;             // Params: UnitId, RemainingIceLayers
    private Action<Vector2Int, int> OnCrateDurabilityChanged; // Params: GridPosition, RemainingDurability
    private Action<int, int> OnLockKeyCollected;          // Params: LockedUnitId, CollectedKeyUnitId

    // --- Structural Feedback Transaction Packets ---
    public enum EngineEventType
    {
        DumplingMovedToContainer,
        ContainerResolved,
        UnitUnblocked,
        LevelWon,
        LevelLost,
        PipeEmittedUnit,
        CrateDamaged,
        CrateDestroyed,
        IceDamaged,
        IceShattered
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
    public void InitializeLevel(
        GameLevelSchema levelData,
        Action<int> onUnitUnblocked = null,
        Action<int, int> onUnitIceChanged = null,
        Action<Vector2Int, int> onCrateDurabilityChanged = null,
        Action<int, int> onLockKeyCollected = null)
    {
        ActiveLevelData = levelData;
        VirtualBelt = new List<GameLevelSchema.DumplingItem>();
        PlayedUnitIds = new HashSet<int>();
        IsGameOver = false;
        IsGameWon = false;

        // Assign optional callbacks
        OnUnitUnblocked = onUnitUnblocked;
        OnUnitIceChanged = onUnitIceChanged;
        OnCrateDurabilityChanged = onCrateDurabilityChanged;
        OnLockKeyCollected = onLockKeyCollected;

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
        
        if (activeUnit == null || activeUnit.IceLayers > 0 || PlayedUnitIds.Contains(activeUnit.UnitId)) 
            return outputTransactionHistory;

        // 1. Collect all related linked units into an evaluation block
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
                    if (foundLinkedUnit != null && foundLinkedUnit.IceLayers == 0)
                    {
                        visitedClusterIds.Add(linkedId);
                        clusterQueue.Enqueue(foundLinkedUnit);
                    }
                }
            }
        }

        // 2. ATOMIC TRANSACTION RULE: Verify blockers across cluster
        foreach (var clusterUnit in linkedCluster)
        {
            var unitCellNode = FindCellNodeByUnitId(clusterUnit.UnitId);
            if (IsUnitClusterBlocked(unitCellNode.Position, clusterUnit, visitedClusterIds))
            {
                return outputTransactionHistory; 
            }
        }

        List<GameLevelSchema.Coordinate> clearedCoordinates = new List<GameLevelSchema.Coordinate>();

        // 3. Clear verified playable cluster group
        foreach (var currentUnit in linkedCluster)
        {
            PlayedUnitIds.Add(currentUnit.UnitId);

            // Trigger the explicit lock/key dependencies checks before wiping the key unit data
            NotifyLocksOfKeyCollection(currentUnit.UnitId);

            var node = FindCellNodeByUnitId(currentUnit.UnitId);
            if (node != null)
            {
                clearedCoordinates.Add(node.Position);
                node.OccupyingUnit = null; 
            }

            foreach (var dumpling in currentUnit.InteriorContents)
            {
                VirtualBelt.Add(dumpling);
            }
        }

        // 4. Run Downstream updates and process visual alerts
        ProcessAdjacentObstacleImpacts(clearedCoordinates, outputTransactionHistory);
        ProcessBeltResolutionPipeline(outputTransactionHistory);
        ProcessPipeEmissions(outputTransactionHistory);
        EvaluateNewlyUnblockedUnits(outputTransactionHistory);
        EvaluateGameStatusStates(outputTransactionHistory);

        return outputTransactionHistory;
    }

    // --- Processing Impacts on Ice & Crates ---
    private void ProcessAdjacentObstacleImpacts(List<GameLevelSchema.Coordinate> clearedCoords, List<EngineEvent> transactions)
    {
        HashSet<GameLevelSchema.Coordinate> processedNeighbors = new HashSet<GameLevelSchema.Coordinate>();

        foreach (var origin in clearedCoords)
        {
            var neighbors = new GameLevelSchema.Coordinate[]
            {
                new GameLevelSchema.Coordinate(origin.X, origin.Y - 1), 
                new GameLevelSchema.Coordinate(origin.X, origin.Y + 1), 
                new GameLevelSchema.Coordinate(origin.X - 1, origin.Y), 
                new GameLevelSchema.Coordinate(origin.X + 1, origin.Y)  
            };

            foreach (var targetCoord in neighbors)
            {
                if (processedNeighbors.Contains(targetCoord)) continue;
                if (!gridMatrix.TryGetValue(targetCoord, out var neighborCell)) continue;

                processedNeighbors.Add(targetCoord);
                Vector2Int unityCoord = new Vector2Int(targetCoord.X, targetCoord.Y);

                // A. Handle Destructible Crates
                if (neighborCell.IsPlayablePath && neighborCell.CrateDurability > 0)
                {
                    neighborCell.CrateDurability--;
                    
                    // Fire optional callback to GameManager view layer
                    OnCrateDurabilityChanged?.Invoke(unityCoord, neighborCell.CrateDurability);

                    if (neighborCell.CrateDurability == 0)
                    {
                        transactions.Add(new EngineEvent { EventType = EngineEventType.CrateDestroyed, Payload = unityCoord });
                    }
                    else
                    {
                        transactions.Add(new EngineEvent { EventType = EngineEventType.CrateDamaged, TargetId = neighborCell.CrateDurability, Payload = unityCoord });
                    }
                }

                // B. Handle Frozen Ice Units
                if (neighborCell.OccupyingUnit != null && neighborCell.OccupyingUnit.IceLayers > 0)
                {
                    neighborCell.OccupyingUnit.IceLayers--;

                    // Fire optional callback to GameManager view layer
                    OnUnitIceChanged?.Invoke(neighborCell.OccupyingUnit.UnitId, neighborCell.OccupyingUnit.IceLayers);

                    if (neighborCell.OccupyingUnit.IceLayers == 0)
                    {
                        transactions.Add(new EngineEvent { EventType = EngineEventType.IceShattered, TargetId = neighborCell.OccupyingUnit.UnitId, Payload = unityCoord });
                    }
                    else
                    {
                        transactions.Add(new EngineEvent { EventType = EngineEventType.IceDamaged, TargetId = neighborCell.OccupyingUnit.UnitId, QueueIndex = neighborCell.OccupyingUnit.IceLayers, Payload = unityCoord });
                    }
                }
            }
        }
    }

    // --- Key/Lock Evaluation Event Despatcher ---
    private void NotifyLocksOfKeyCollection(int keyId)
    {
        if (OnLockKeyCollected == null) return;

        // Scan all remaining locked cells to check if they were listening for this specific Key ID
        foreach (var cellNode in ActiveLevelData.Grid.Matrix)
        {
            if (cellNode.OccupyingUnit != null && !PlayedUnitIds.Contains(cellNode.OccupyingUnit.UnitId))
            {
                if (cellNode.OccupyingUnit.ExplicitlyBlockedByUnitIds.Contains(keyId))
                {
                    OnLockKeyCollected.Invoke(cellNode.OccupyingUnit.UnitId, keyId);
                }
            }
        }
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
                            transactions.Add(new EngineEvent { EventType = EngineEventType.ContainerResolved, TargetId = activeContainer.Id, QueueIndex = q });
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

    private void ProcessPipeEmissions(List<EngineEvent> transactions)
    {
        foreach (var cellNode in ActiveLevelData.Grid.Matrix)
        {
            if (cellNode.ContinuousPipe != null && cellNode.ContinuousPipe.ReservoirQueue.Count > 0)
            {
                var spaceAboveCoord = new GameLevelSchema.Coordinate(cellNode.Position.X, cellNode.Position.Y - 1);
                
                if (gridMatrix.TryGetValue(spaceAboveCoord, out var spaceAboveNode))
                {
                    if (spaceAboveNode.IsPlayablePath && spaceAboveNode.CrateDurability == 0 && spaceAboveNode.OccupyingUnit == null)
                    {
                        var nextUnit = cellNode.ContinuousPipe.ReservoirQueue[0];
                        cellNode.ContinuousPipe.ReservoirQueue.RemoveAt(0);

                        spaceAboveNode.OccupyingUnit = nextUnit;

                        transactions.Add(new EngineEvent
                        {
                            EventType = EngineEventType.PipeEmittedUnit,
                            TargetId = nextUnit.UnitId,
                            Payload = new Vector2Int(spaceAboveNode.Position.X, spaceAboveNode.Position.Y)
                        });
                    }
                }
            }
        }
    }

    private void EvaluateNewlyUnblockedUnits(List<EngineEvent> transactions)
    {
        foreach (var cellNode in ActiveLevelData.Grid.Matrix)
        {
            if (cellNode.OccupyingUnit == null || cellNode.OccupyingUnit.IceLayers > 0 || PlayedUnitIds.Contains(cellNode.OccupyingUnit.UnitId)) continue;

            if (!IsUnitClusterBlocked(cellNode.Position, cellNode.OccupyingUnit, new HashSet<int>()))
            {
                // Fire optional view notification callback
                OnUnitUnblocked?.Invoke(cellNode.OccupyingUnit.UnitId);

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
    public bool IsUnitClusterBlocked(GameLevelSchema.Coordinate coord, GameLevelSchema.GridUnit unit, HashSet<int> currentClusterIds)
    {
        if (unit.IceLayers > 0) return true;

        foreach (var dependencyId in unit.ExplicitlyBlockedByUnitIds)
        {
            if (!PlayedUnitIds.Contains(dependencyId) && !currentClusterIds.Contains(dependencyId))
            {
                return true; 
            }
        }

        if (coord.Y == 0) return false; 

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

                if (gridMatrix.TryGetValue(neighborCoord, out var neighborCell) && neighborCell.IsPlayablePath)
                {
                    if (neighborCell.CrateDurability > 0 || neighborCell.CrateDurability == -1) continue;
                    if (neighborCell.ContinuousPipe != null && neighborCell.ContinuousPipe.ReservoirQueue.Count > 0) continue;
                    if (neighborCell.OccupyingUnit != null && neighborCell.OccupyingUnit.IceLayers > 0) continue;

                    if (neighborCell.OccupyingUnit == null || PlayedUnitIds.Contains(neighborCell.OccupyingUnit.UnitId))
                    {
                        if (neighborCoord.Y == 0) return false; 

                        visited.Add(neighborCoord);
                        queue.Enqueue(neighborCoord);
                    }
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