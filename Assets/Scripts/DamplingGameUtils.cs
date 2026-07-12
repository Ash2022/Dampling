using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class DamplingGameUtils
{
    /// <summary>
    /// Performs a high-performance, memory-safe deep clone of a level schema configuration 
    /// without utilizing intermediate string serialization layers.
    /// </summary>
    public static GameLevelSchema CloneLevelSchema(GameLevelSchema source)
    {
        if (source == null) return null;

        GameLevelSchema copy = new GameLevelSchema
        {
            LevelId = source.LevelId,
            LevelName = source.LevelName,
            ConveyorBeltMaxCapacity = source.ConveyorBeltMaxCapacity,
            ResolutionQueues = source.ResolutionQueues.Select(q => q.Select(c => new GameLevelSchema.ContainerData 
            { 
                Id = c.Id, 
                ColorIndex = c.ColorIndex,
                Capacity = c.Capacity 
            }).ToList()).ToList()
        };

        copy.Grid.Columns = source.Grid.Columns;
        copy.Grid.Rows = source.Grid.Rows;

        foreach (var sourceNode in source.Grid.Matrix)
        {
            var copyNode = new GameLevelSchema.CellNode
            {
                Position = new GameLevelSchema.Coordinate(sourceNode.Position.X, sourceNode.Position.Y),
                IsPlayablePath = sourceNode.IsPlayablePath
            };

            // Deep clone the continuous pipe asset properties if present
            if (sourceNode.ContinuousPipe != null)
            {
                copyNode.ContinuousPipe = new GameLevelSchema.PipeGenerator
                {
                    Id = sourceNode.ContinuousPipe.Id,
                    MaxTotalEmissions = sourceNode.ContinuousPipe.MaxTotalEmissions,
                    ReservoirQueue = sourceNode.ContinuousPipe.ReservoirQueue.Select(u => new GameLevelSchema.GridUnit
                    {
                        UnitId = u.UnitId,
                        IsHiddenUntilUnblocked = u.IsHiddenUntilUnblocked,
                        InteriorContents = u.InteriorContents.Select(d => new GameLevelSchema.DumplingItem { ColorIndex = d.ColorIndex }).ToList()
                    }).ToList()
                };
            }

            // Deep clone the physical grid units and their relational dependency blocks
            if (sourceNode.OccupyingUnit != null)
            {
                copyNode.OccupyingUnit = new GameLevelSchema.GridUnit
                {
                    UnitId = sourceNode.OccupyingUnit.UnitId,
                    IsHiddenUntilUnblocked = sourceNode.OccupyingUnit.IsHiddenUntilUnblocked,
                    InteriorContents = sourceNode.OccupyingUnit.InteriorContents.Select(d => new GameLevelSchema.DumplingItem { ColorIndex = d.ColorIndex }).ToList(),
                    ExplicitlyBlockedByUnitIds = sourceNode.OccupyingUnit.ExplicitlyBlockedByUnitIds.ToList(),
                    LinkedUnitIds = sourceNode.OccupyingUnit.LinkedUnitIds.ToList()
                };
            }

            // Add the fully cloned node to the new matrix list
            copy.Grid.Matrix.Add(copyNode);
        }

        return copy;
    }

    public static Color GetColorByIndex(int colorIndex)
    {
        if (colorIndex < 0)
        {
            return new Color(0.85f, 0.85f, 0.85f, 1f); // Empty fallback gray
        }

        return colorIndex switch
        {
            0 => new Color(0.85f, 0.23f, 0.23f), // Vivid Red
            1 => new Color(0.18f, 0.67f, 0.18f), // Vivid Green
            2 => new Color(0.14f, 0.38f, 0.78f), // Deep Blue
            3 => new Color(0.88f, 0.72f, 0.12f), // Clear Yellow
            4 => new Color(0.53f, 0.18f, 0.68f), // Purple
            5 => new Color(0.12f, 0.72f, 0.72f), // Teal
            6 => new Color(0.88f, 0.45f, 0.12f), // Orange
            7 => new Color(0.44f, 0.26f, 0.12f), // Brown
            8 => new Color(0.88f, 0.12f, 0.56f), // Pink
            _ => new Color(0.58f, 0.63f, 0.67f)  // Slate
        };
    }
}