using System;
using System.Collections.Generic;
using UnityEngine;
using static GameLevelSchema;

public class LevelVisualization : MonoBehaviour
{
    [Header("Visual Prefabs")]
    public GameObject UnitPrefab;
    public GameObject ContainerPrefab;

    [Header("Manual Y-Axis Baselines")]
    public float QueueBottomY = 3.0f;
    public float GridTopY = -1.0f;

    private List<GameObject> spawnedVisualElements = new List<GameObject>();

    public BoardVisualReferences RenderInitialBoard(GameLevelSchema levelData)
    {
        ClearCurrentVisualization();
        
        // Instantiate the packet to return tracking data straight to GameManager
        BoardVisualReferences references = new BoardVisualReferences();

        Vector2 unitSize = GetPrefabSize(UnitPrefab);
        Vector2 containerSize = GetPrefabSize(ContainerPrefab);

        // 1. GENERATE AND CENTER DEMAND QUEUES
        int totalQueues = levelData.ResolutionQueues.Count;
        float totalQueuesWidth = totalQueues * containerSize.x;
        float queueStartX = -(totalQueuesWidth / 2f) + (containerSize.x / 2f);

        for (int q = 0; q < totalQueues; q++)
        {
            float targetX = queueStartX + (q * containerSize.x);
            var activeQueueList = levelData.ResolutionQueues[q];

            for (int c = 0; c < activeQueueList.Count; c++)
            {
                float targetY = QueueBottomY + (c * containerSize.y);
                Vector3 spawnPosition = new Vector3(targetX, targetY, 0f);

                GameObject containerInstance = DamplingObjectPool.Instance.GetContainer(spawnPosition, Quaternion.identity, transform);
                spawnedVisualElements.Add(containerInstance);

                ContainerView containerView = containerInstance.GetComponent<ContainerView>();
                containerView.Initialize(activeQueueList[c]);

                containerInstance.name = $"Container_Q{q}_Idx{c}_{activeQueueList[c].ColorId}";

                // Map reference by unique ID for instant event resolution
                references.ContainerViews.Add(activeQueueList[c].Id, containerView);
            }
        }

        // 2. GENERATE AND CENTER SUPPLY GRID MAP
        int columns = levelData.Grid.Columns;
        float totalGridWidth = columns * unitSize.x;
        float gridStartX = -(totalGridWidth / 2f) + (unitSize.x / 2f);

        foreach (var cellNode in levelData.Grid.Matrix)
        {
            if (!cellNode.IsPlayablePath) continue;
            int gridX = cellNode.Position.X;
            int gridY = cellNode.Position.Y;
            Vector2Int coord = new Vector2Int(gridX, gridY);

            float worldX = gridStartX + (gridX * unitSize.x);
            float worldY = GridTopY - (gridY * unitSize.y);
            Vector3 spawnPosition = new Vector3(worldX, worldY, 0f);

            GameObject unitInstance = DamplingObjectPool.Instance.GetUnit(spawnPosition, Quaternion.identity, transform);
            spawnedVisualElements.Add(unitInstance);

            UnitView unitView = unitInstance.GetComponent<UnitView>();
            unitView.Initialize(cellNode);

            unitInstance.name = cellNode.ContinuousPipe != null ? $"PipeUnit_({gridX},{gridY})" :
                                cellNode.OccupyingUnit != null ? $"StandardUnit_({gridX},{gridY})" : 
                                $"EmptyCell_({gridX},{gridY})";

            // Map reference by space tracking coordinate
            references.UnitViews.Add(coord, unitView);
        }

        return references;
    }

    public void ClearCurrentVisualization()
    {
        foreach (var element in spawnedVisualElements)
        {
            if (element != null)
            {
                // SWAPPED: Destroy/DestroyImmediate calls replaced with pool recycling
                if (element.GetComponent<UnitView>() != null)
                {
                    DamplingObjectPool.Instance.ReturnUnit(element);
                }
                else if (element.GetComponent<ContainerView>() != null)
                {
                    DamplingObjectPool.Instance.ReturnContainer(element);
                }
                else
                {
                    if (Application.isPlaying) Destroy(element);
                    else DestroyImmediate(element);
                }
            }
        }
        spawnedVisualElements.Clear();
    }

    private Vector2 GetPrefabSize(GameObject prefab)
    {
        if (prefab == null) return Vector2.one;
        var spriteRenderer = prefab.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            return spriteRenderer.bounds.size;
        }
        return Vector2.one;
    }
}