using System;
using System.Collections.Generic;
using UnityEngine;

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

    public bool HardLevel;

    // =========================================================================
    // INNER CORE TYPES
    // =========================================================================

    public class ContainerData
    {
        public int Id { get; set; }
        public int ColorIndex { get; set; } = -1;
        public int Capacity { get; set; } = 3;
        public int FilledSlotsCount { get; set; } = 0;
    }

    public class GridTopology
    {
        public int Columns { get; set; }
        public int Rows { get; set; }

        // Maps physical coordinates to specific cell behaviors
        public List<CellNode> Matrix { get; set; } = new List<CellNode>();
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
        // The flat, simple identity number of this unit (0, 1, 2...)
        // If it's unassigned or empty, it defaults strictly to -1
        public int UnitId { get; set; } = -1;

        public bool IsHiddenUntilUnblocked { get; set; } = false;

        // Content Layer
        public List<DumplingItem> InteriorContents { get; set; } = new List<DumplingItem>();

        // Stores the plain integer IDs of the Key units that block this unit
        public List<int> ExplicitlyBlockedByUnitIds { get; set; } = new List<int>();

        // Stores the plain integer IDs of the partner units linked to this unit
        public List<int> LinkedUnitIds { get; set; } = new List<int>();

        /// <summary>
        /// 0 = Normal active/thawed unit.
        /// >0 = Unit is frozen in ice. It cannot be clicked or pathfind until this hits 0.
        /// </summary>
        public int IceLayers { get; set; } = 0;
    }

    public class DumplingItem
    {
        public int ColorIndex { get; set; } = -1;
    }

    public class PipeGenerator
    {
        public int Id { get; set; }

        // The sequence of units queued inside the pipe to emit into the grid when space clears
        public List<GridUnit> ReservoirQueue { get; set; } = new List<GridUnit>();

        // Limits how many units this generator can emit over its lifetime (infinite if null)
        public int? MaxTotalEmissions { get; set; }
    }

    public class BoardVisualReferences
    {
        public Dictionary<Vector2Int, UnitView> UnitViews { get; set; } = new Dictionary<Vector2Int, UnitView>();
        public Dictionary<int, ContainerView> ContainerViews { get; set; } = new Dictionary<int, ContainerView>();

        public Dictionary<ContainerView, Vector3> logicalContainerPositions = new Dictionary<ContainerView, Vector3>();
    }
}