using System;
using System.Collections.Generic;

// =========================================================================
// UNIFIED LEVEL CONFIGURATION MODEL
// =========================================================================
public class GameLevelSchema
{
    // --- Level Metadata ---
    public int LevelId { get; set; }
    public string LevelName { get; set; }
    public int ConveyorBeltMaxCapacity { get; set; } = 7;

    // --- Demand Side (Resolution Queues) ---
    // A list of distinct queues, where each queue contains an ordered sequence of containers.
    public List<List<ContainerData>> ResolutionQueues { get; set; } = new List<List<ContainerData>>();

    // --- Supply Side (The Grid Board Topology) ---
    public GridTopology Grid { get; set; } = new GridTopology();

    // =========================================================================
    // INNER CORE TYPES
    // =========================================================================

    public class ContainerData
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string ColorId { get; set; }
        public int Capacity { get; set; } = 3;
    }

    public class GridTopology
    {
        public int Columns { get; set; }
        public int Rows { get; set; }
        
        // Maps physical coordinates to specific cell behaviors
        public Dictionary<Coordinate, CellNode> Matrix { get; set; } = new Dictionary<Coordinate, CellNode>();
    }

    public struct Coordinate
    {
        public int X { get; set; }
        public int Y { get; set; } // Y=0 typically represents the playable exit row
        
        public Coordinate(int x, int y) { X = x; Y = y; }
    }

    public class CellNode
    {
        public Coordinate Position { get; set; }
        public bool IsPlayablePath { get; set; } = true; // False creates un-passable gaps/holes in the map
        
        // The interactive content occupying this cell space
        public GridUnit OccupyingUnit { get; set; }
        
        // Pipe/Dispenser Behavior: If assigned, this cell behaves as an infinite or finite generator
        public PipeGenerator ContinuousPipe { get; set; }
    }

    public class GridUnit
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public bool IsHiddenUntilUnblocked { get; set; } = false;

        // Content Layer (Supports single color or multi-color arrangements)
        public List<DumplingItem> InteriorContents { get; set; } = new List<DumplingItem>();

        // Explicit Dependency Graph overrides (For non-trivial structural locks)
        public List<Guid> ExplicitlyBlockedByUnitIds { get; set; } = new List<Guid>();
        
        // Linkage Feature: Triggering this unit forces the simultaneous play of these targeted units
        public List<Guid> LinkedUnitIds { get; set; } = new List<Guid>();
    }

    public class DumplingItem
    {
        public string ColorId { get; set; }
    }

    public class PipeGenerator
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        // The sequence of units queued inside the pipe to emit into the grid when space clears
        public List<GridUnit> ReservoirQueue { get; set; } = new List<GridUnit>();
        
        // Limits how many units this generator can emit over its lifetime (infinite if null)
        public int? MaxTotalEmissions { get; set; } 
    }
}