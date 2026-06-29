using System;
using System.Collections.Generic;
using System.Linq;

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
                ColorId = c.ColorId, 
                Capacity = c.Capacity 
            }).ToList()).ToList()
        };

        copy.Grid.Columns = source.Grid.Columns;
        copy.Grid.Rows = source.Grid.Rows;

        foreach (var kvp in source.Grid.Matrix)
        {
            var sourceNode = kvp.Value;
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
                        Id = u.Id,
                        IsHiddenUntilUnblocked = u.IsHiddenUntilUnblocked,
                        InteriorContents = u.InteriorContents.Select(d => new GameLevelSchema.DumplingItem { ColorId = d.ColorId }).ToList()
                    }).ToList()
                };
            }

            // Deep clone the physical grid units and their relational dependency blocks
            if (sourceNode.OccupyingUnit != null)
            {
                copyNode.OccupyingUnit = new GameLevelSchema.GridUnit
                {
                    Id = sourceNode.OccupyingUnit.Id,
                    IsHiddenUntilUnblocked = sourceNode.OccupyingUnit.IsHiddenUntilUnblocked,
                    InteriorContents = sourceNode.OccupyingUnit.InteriorContents.Select(d => new GameLevelSchema.DumplingItem { ColorId = d.ColorId }).ToList(),
                    ExplicitlyBlockedByUnitIds = sourceNode.OccupyingUnit.ExplicitlyBlockedByUnitIds.ToList(),
                    LinkedUnitIds = sourceNode.OccupyingUnit.LinkedUnitIds.ToList()
                };
            }

            // Map back to the new matrix structure safely using clean copy coordinates
            copy.Grid.Matrix.Add(new GameLevelSchema.Coordinate(kvp.Key.X, kvp.Key.Y), copyNode);
        }

        return copy;
    }
}