using System;
using System.Collections.Generic;
using UnityEngine;
using static GameLevelSchema;

public class LevelVisualization : MonoBehaviour
{
    [Header("Visual Prefabs")]
    public GameObject UnitPrefab;
    public GameObject FramePrefab;
    public GameObject ContainerPrefab;

    [Header("Manual Y-Axis Baselines")]
    public float QueueBottomY = 3.0f;
    public float GridTopY = -1.0f;

    float ScaleFactor = 1.2f;
    private List<GameObject> spawnedVisualElements = new List<GameObject>();

    public BoardVisualReferences RenderInitialBoard(GameLevelSchema levelData)
    {
        ClearCurrentVisualization();

        // Instantiate the packet to return tracking data straight to GameManager
        BoardVisualReferences references = new BoardVisualReferences();

        

        Vector2 unitSize = GetPrefabSize(UnitPrefab)*ScaleFactor;
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
                containerView.Initialize(activeQueueList[c], q);

                containerInstance.name = $"Container_Q{q}_Idx{c}_{activeQueueList[c].ColorId}";

                // Map reference by unique ID for instant event resolution
                references.ContainerViews.Add(activeQueueList[c].Id, containerView);
            }
        }

        // 2. GENERATE AND CENTER SUPPLY GRID MAP
        int columns = levelData.Grid.Columns;

        // A. Dynamically scan the active matrix to find the true physical layout edges
        int minX = int.MaxValue;
        int maxX = int.MinValue;

        foreach (var cellNode in levelData.Grid.Matrix)
        {
            if (cellNode.Position.X < minX) minX = cellNode.Position.X;
            if (cellNode.Position.X > maxX) maxX = cellNode.Position.X;
        }

        // If the grid matrix is empty, fallback safely to standard behavior
        if (minX == int.MaxValue) { minX = 0; maxX = columns - 1; }

        // B. Calculate the true center based on actual active span
        float physicalWidth = (maxX - minX) * unitSize.x;
        float gridStartX = -(physicalWidth / 2f) - (minX * unitSize.x);

        foreach (var cellNode in levelData.Grid.Matrix)
        {
            int gridX = cellNode.Position.X;
            int gridY = cellNode.Position.Y;
            Vector2Int coord = new Vector2Int(gridX, gridY);

            float worldX = gridStartX + (gridX * unitSize.x);
            float worldY = GridTopY - (gridY * unitSize.y);
            Vector3 spawnPosition = new Vector3(worldX, worldY, 0f);

            // 1. If it's a valid playable path, spawn the active game units or pipes
            if (cellNode.IsPlayablePath)
            {
                GameObject unitInstance = DamplingObjectPool.Instance.GetUnit(spawnPosition, Quaternion.identity, transform);
                spawnedVisualElements.Add(unitInstance);

                UnitView unitView = unitInstance.GetComponent<UnitView>();
                unitView.Initialize(cellNode);

                unitInstance.name = cellNode.ContinuousPipe != null ? $"PipeUnit_({gridX},{gridY})" :
                                    cellNode.OccupyingUnit != null ? $"StandardUnit_({gridX},{gridY})" :
                                    $"EmptyCell_({gridX},{gridY})";

                // Map reference by space tracking coordinate for later link-rendering pass
                references.UnitViews.Add(coord, unitView);
            }
            // 2. If it's NOT a playable path (blocked gap/hole), inline spawn the structural EmptyUnit instead
            else
            {

            }
        }

        // --- SAFE SECOND PASS: Handshake and view lookup happen entirely here ---
        foreach (var cellNode in levelData.Grid.Matrix)
        {
            // Skip if empty space, blocker, or if the unit has no links
            if (!cellNode.IsPlayablePath || cellNode.OccupyingUnit == null) continue;
            if (cellNode.OccupyingUnit.LinkedUnitIds == null || cellNode.OccupyingUnit.LinkedUnitIds.Count == 0) continue;

            Vector2Int coord = new Vector2Int(cellNode.Position.X, cellNode.Position.Y);
            if (references.UnitViews.TryGetValue(coord, out UnitView myView) && myView != null)
            {
                foreach (var partnerId in cellNode.OccupyingUnit.LinkedUnitIds)
                {
                    // 1. Strict integer handshake check
                    if (cellNode.OccupyingUnit.UnitId > partnerId)
                    {
                        // 2. Find the partner view right here using the local parameter
                        UnitView partnerView = null;
                        foreach (var view in references.UnitViews.Values)
                        {
                            if (view != null && view.UnitId == partnerId)
                            {
                                partnerView = view;
                                break;
                            }
                        }

                        // 3. Only call the view if the partner actually exists on the board
                        if (partnerView != null)
                        {
                            myView.RenderLinkLines(partnerView);
                        }
                    }
                }
            }
        }

        GenerateFramePass(levelData, unitSize);

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

    private void GenerateFramePass(GameLevelSchema levelData, Vector2 unitSize)
    {
        
        HashSet<Vector2Int> playableMap = new HashSet<Vector2Int>();
        int minX = int.MaxValue, maxX = int.MinValue, minY = 0, maxY = int.MinValue;

        foreach (var cell in levelData.Grid.Matrix)
        {
            if (cell.IsPlayablePath)
            {
                playableMap.Add(new Vector2Int(cell.Position.X, cell.Position.Y));
                minX = Math.Min(minX, cell.Position.X);
                maxX = Math.Max(maxX, cell.Position.X);
                maxY = Math.Max(maxY, cell.Position.Y);
            }
        }

        float physicalWidth = (maxX - minX) * unitSize.x;
        float gridStartX = -(physicalWidth / 2f) - (minX * unitSize.x);

        // NEW: Dictionary to keep track of the frames we spawn for the post-pass
        Dictionary<Vector2Int, FrameView> activeFrames = new Dictionary<Vector2Int, FrameView>();

        for (int x = -3; x < 10; x++)
        {
            for (int y = minY; y <= 9; y++)
            {
                Vector2Int coord = new Vector2Int(x, y);

                if (!playableMap.Contains(coord))
                {
                    float worldX = gridStartX + (x * unitSize.x);
                    float worldY = GridTopY - (y * unitSize.y);
                    Vector3 spawnPos = new Vector3(worldX, worldY, 0f);

                    GameObject frameInstance = Instantiate(FramePrefab, spawnPos, Quaternion.identity, transform);
                    
                    frameInstance.transform.localScale*=ScaleFactor;

                    spawnedVisualElements.Add(frameInstance);

                    FrameView fv = frameInstance.GetComponent<FrameView>();
                    if (fv != null)
                    {
                        bool left = !playableMap.Contains(new Vector2Int(x - 1, y));
                        bool right = !playableMap.Contains(new Vector2Int(x + 1, y));
                        bool up = !playableMap.Contains(new Vector2Int(x, y - 1));
                        bool down = !playableMap.Contains(new Vector2Int(x, y + 1));
                        bool upLeft = !playableMap.Contains(new Vector2Int(x - 1, y - 1));
                        bool upRight = !playableMap.Contains(new Vector2Int(x + 1, y - 1));
                        bool downLeft = !playableMap.Contains(new Vector2Int(x - 1, y + 1));
                        bool downRight = !playableMap.Contains(new Vector2Int(x + 1, y + 1));

                        fv.ApplyFrameMask(left, right, up, down, upLeft, upRight, downLeft, downRight);

                        // Add it to our dictionary
                        activeFrames[coord] = fv;
                    }
                }
            }
        }

        // NEW: Run the post-pass fix
        ApplyTopRowCaps(activeFrames, playableMap, -3, 11, minY);
    }

    private void ApplyTopRowCaps(Dictionary<Vector2Int, FrameView> activeFrames, HashSet<Vector2Int> playableMap, int minX, int maxX, int topY)
    {
        // Iterate purely across the top row of the grid
        for (int x = minX - 1; x <= maxX + 1; x++)
        {
            Vector2Int coord = new Vector2Int(x, topY);

            if (activeFrames.TryGetValue(coord, out FrameView fv))
            {
                // Check if there are playable paths flanking this frame block
                // (Contains == true means there IS a path)
                bool pathLeft = playableMap.Contains(new Vector2Int(x - 1, topY));
                bool pathRight = playableMap.Contains(new Vector2Int(x + 1, topY));

                // Force the cap logic
                fv.ApplyTopRowOverride(pathLeft, pathRight);
            }
        }
    }

}