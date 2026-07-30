using System;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using static GameLevelSchema;
using System.Linq;

public class LevelVisualization : MonoBehaviour
{
    [Header("Visual Prefabs")]
    public GameObject UnitPrefab;
    public GameObject FramePrefab;
    public GameObject ContainerPrefab;

    [Header("Manual Y-Axis Baselines")]
    public float QueueBottomY = 3.0f;
    public float GridTopY = -1.0f;
    public float ContainerSpacingY = 0.57f;

    private float ScaleFactor = 1.1f;


    private List<UnitView> activeUnits = new List<UnitView>();
    private List<FrameView> activeFrames = new List<FrameView>();
    private List<ContainerView> activeContainers = new List<ContainerView>();

    BoardVisualReferences references;

    public BoardVisualReferences RenderInitialBoard(GameLevelSchema levelData,int levelIndex)
    {
        ClearCurrentVisualization();

        references = new BoardVisualReferences();
        references.ContainerQueues = new List<List<ContainerView>>();

        if(levelIndex>10)
            levelData = ApplyDynamicColorMapping(levelData,12);

        if(levelIndex>50 && levelData.MetaData!=null && levelData.MetaData.WinRate<1f)
            levelData.HardLevel = true;
        Vector2 unitSize = GetPrefabSize(UnitPrefab) * ScaleFactor;
        Vector2 containerSize = GetPrefabSize(ContainerPrefab);
        float containerSpacingX = 0.1f;

        int totalQueues = levelData.ResolutionQueues.Count;
        float queueStartX = -((totalQueues - 1) * (containerSize.x + containerSpacingX)) / 2f;

        for (int q = 0; q < totalQueues; q++)
        {
            var viewQueue = new List<ContainerView>();
            float targetX = queueStartX + (q * (containerSize.x + containerSpacingX));
            var activeQueueList = levelData.ResolutionQueues[q];

            for (int c = 0; c < activeQueueList.Count; c++)
            {
                float targetY = QueueBottomY + (c * ContainerSpacingY);
                Vector3 spawnPosition = new Vector3(targetX, targetY, 0f);

                GameObject containerInstance = DamplingObjectPool.Instance.GetContainer(spawnPosition, Quaternion.identity, transform);
                ContainerView containerView = containerInstance.GetComponent<ContainerView>();

                containerView.Initialize(activeQueueList[c], q);
                containerInstance.name = $"Container_Q{q}_Idx{c}_{activeQueueList[c].ColorIndex}";

                activeContainers.Add(containerView);
                viewQueue.Add(containerView);
            }
            references.ContainerQueues.Add(viewQueue);
        }

        int columns = levelData.Grid.Columns;
        int minX = int.MaxValue;
        int maxX = int.MinValue;

        foreach (var cellNode in levelData.Grid.Matrix)
        {
            if (cellNode.Position.X < minX) minX = cellNode.Position.X;
            if (cellNode.Position.X > maxX) maxX = cellNode.Position.X;
        }

        if (minX == int.MaxValue) { minX = 0; maxX = columns - 1; }

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

            if (cellNode.IsPlayablePath)
            {
                GameObject unitInstance = DamplingObjectPool.Instance.GetUnit(spawnPosition, Quaternion.identity, transform);
                UnitView unitView = unitInstance.GetComponent<UnitView>();

                unitView.Initialize(cellNode);
                unitInstance.name = cellNode.ContinuousPipe != null ? $"PipeUnit_({gridX},{gridY})" :
                                    cellNode.OccupyingUnit != null ? $"StandardUnit_({gridX},{gridY})" :
                                    $"EmptyCell_({gridX},{gridY})";

                activeUnits.Add(unitView);
                references.UnitViews.Add(coord, unitView);
            }
        }

        foreach (var cellNode in levelData.Grid.Matrix)
        {
            if (!cellNode.IsPlayablePath || cellNode.OccupyingUnit == null ||
             cellNode.OccupyingUnit.LinkedUnitIds == null ||
              cellNode.OccupyingUnit.LinkedUnitIds.Count == 0) continue;

            Vector2Int coord = new Vector2Int(cellNode.Position.X, cellNode.Position.Y);
            UnitView myView = references.UnitViews[coord];

            foreach (var partnerId in cellNode.OccupyingUnit.LinkedUnitIds)
            {
                if (cellNode.OccupyingUnit.UnitId > partnerId)
                {
                    UnitView partnerView = null;
                    foreach (var view in references.UnitViews.Values)
                    {
                        if (view.UnitId == partnerId)
                        {
                            partnerView = view;
                            break;
                        }
                    }

                    myView.RenderLinkLines(partnerView);
                }
            }
        }

        GenerateFramePass(levelData, unitSize);

        return references;
    }

    public void ClearCurrentVisualization()
    {
        foreach (var unit in activeUnits)
        {
            DamplingObjectPool.Instance.ReturnUnit(unit.gameObject);
        }
        activeUnits.Clear();

        foreach (var container in activeContainers)
        {
            DamplingObjectPool.Instance.ReturnContainer(container.gameObject);
        }
        activeContainers.Clear();

        foreach (var frame in activeFrames)
        {
            DamplingObjectPool.Instance.ReturnFrame(frame.gameObject);
        }
        activeFrames.Clear();
    }

    private Vector2 GetPrefabSize(GameObject prefab)
    {
        return prefab.GetComponent<SpriteRenderer>().bounds.size;
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

        Dictionary<Vector2Int, FrameView> currentPassFrames = new Dictionary<Vector2Int, FrameView>();

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

                    GameObject frameInstance = DamplingObjectPool.Instance.GetFrame(spawnPos, Quaternion.identity, transform);
                    frameInstance.transform.localScale = FramePrefab.transform.localScale * ScaleFactor;

                    FrameView fv = frameInstance.GetComponent<FrameView>();

                    bool left = !playableMap.Contains(new Vector2Int(x - 1, y));
                    bool right = !playableMap.Contains(new Vector2Int(x + 1, y));
                    bool up = !playableMap.Contains(new Vector2Int(x, y - 1));
                    bool down = !playableMap.Contains(new Vector2Int(x, y + 1));
                    bool upLeft = !playableMap.Contains(new Vector2Int(x - 1, y - 1));
                    bool upRight = !playableMap.Contains(new Vector2Int(x + 1, y - 1));
                    bool downLeft = !playableMap.Contains(new Vector2Int(x - 1, y + 1));
                    bool downRight = !playableMap.Contains(new Vector2Int(x + 1, y + 1));

                    fv.ApplyFrameMask(left, right, up, down, upLeft, upRight, downLeft, downRight);

                    currentPassFrames[coord] = fv;
                    activeFrames.Add(fv);
                }
            }
        }

        ApplyTopRowCaps(currentPassFrames, playableMap, -3, 11, minY);
    }

    private void ApplyTopRowCaps(Dictionary<Vector2Int, FrameView> frameDict, HashSet<Vector2Int> playableMap, int minX, int maxX, int topY)
    {
        for (int x = minX - 1; x <= maxX + 1; x++)
        {
            Vector2Int coord = new Vector2Int(x, topY);

            if (frameDict.TryGetValue(coord, out FrameView fv))
            {
                bool pathLeft = playableMap.Contains(new Vector2Int(x - 1, topY));
                bool pathRight = playableMap.Contains(new Vector2Int(x + 1, topY));
                fv.ApplyTopRowOverride(pathLeft, pathRight);
            }
        }
    }

    public void AdvanceContainerQueue(int queueIndex, ContainerView resolvedView)
    {
        List<ContainerView> targetQueue = references.ContainerQueues[queueIndex];

        targetQueue.Remove(resolvedView);
        activeContainers.Remove(resolvedView);
        DamplingObjectPool.Instance.ReturnContainer(resolvedView.gameObject);

        for (int i = 0; i < targetQueue.Count; i++)
        {
            ContainerView container = targetQueue[i];
            float targetY = QueueBottomY + (i * ContainerSpacingY);
            Vector3 newTargetPos = new Vector3(container.transform.position.x, targetY, 0f);

            container.transform.DOKill();
            container.transform.DOMove(newTargetPos, 0.3f).SetEase(Ease.OutBack);

            if (i == 0)
            {
                container.RevealContainerColor();
            }
        }
    }

    internal void AddPipeElement(UnitView unitView)
    {
        //we added a unit from the revive flow 
        activeUnits.Add(unitView);
    }

    public Vector3 GetUnitWorldPosition(int gridX, int gridY, GameLevelSchema levelData)
    {

        Vector2 unitSize = GetPrefabSize(UnitPrefab) * ScaleFactor;

        int minX = int.MaxValue;
        int maxX = int.MinValue;

        foreach (var cellNode in levelData.Grid.Matrix)
        {
            if (cellNode.Position.X < minX) minX = cellNode.Position.X;
            if (cellNode.Position.X > maxX) maxX = cellNode.Position.X;
        }

        if (minX == int.MaxValue)
        {
            minX = 0;
            maxX = levelData.Grid.Columns - 1;
        }

        float physicalWidth = (maxX - minX) * unitSize.x;
        float gridStartX = -(physicalWidth / 2f) - (minX * unitSize.x);

        float worldX = gridStartX + (gridX * unitSize.x);
        float worldY = GridTopY - (gridY * unitSize.y);


        return new Vector3(worldX, worldY, 0f);
    }


    public GameLevelSchema ApplyDynamicColorMapping(GameLevelSchema runtimeLevelCopy, int totalAvailableColors = 12)
    {
        System.Random seededRng = new System.Random(runtimeLevelCopy.LevelName.GetHashCode());

        HashSet<int> existingColors = new HashSet<int>();

        foreach (var node in runtimeLevelCopy.Grid.Matrix)
        {
            if (node.ContinuousPipe != null)
            {
                foreach (var unit in node.ContinuousPipe.ReservoirQueue)
                    foreach (var dumpling in unit.InteriorContents)
                        existingColors.Add(dumpling.ColorIndex);
            }
            else if (node.OccupyingUnit != null)
            {
                foreach (var dumpling in node.OccupyingUnit.InteriorContents)
                    existingColors.Add(dumpling.ColorIndex);
            }
        }

        foreach (var queue in runtimeLevelCopy.ResolutionQueues)
        {
            foreach (var container in queue)
            {
                existingColors.Add(container.ColorIndex);
            }
        }

        List<int> globalPalette = Enumerable.Range(0, totalAvailableColors).OrderBy(x => seededRng.Next()).ToList();
        Dictionary<int, int> colorMap = new Dictionary<int, int>();
        int mapIndex = 0;

        foreach (int oldColor in existingColors)
        {
            colorMap[oldColor] = globalPalette[mapIndex++];
        }

        foreach (var node in runtimeLevelCopy.Grid.Matrix)
        {
            if (node.ContinuousPipe != null)
            {
                foreach (var unit in node.ContinuousPipe.ReservoirQueue)
                    foreach (var dumpling in unit.InteriorContents)
                        dumpling.ColorIndex = colorMap[dumpling.ColorIndex];
            }
            else if (node.OccupyingUnit != null)
            {
                foreach (var dumpling in node.OccupyingUnit.InteriorContents)
                    dumpling.ColorIndex = colorMap[dumpling.ColorIndex];
            }
        }

        foreach (var queue in runtimeLevelCopy.ResolutionQueues)
        {
            foreach (var container in queue)
            {
                container.ColorIndex = colorMap[container.ColorIndex];
            }
        }

        return runtimeLevelCopy;
    }
}