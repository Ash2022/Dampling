using DG.Tweening;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BoosterManager
{
    // Dependencies
    private readonly GameManager gameManager;
    private readonly DamplingGameCore gameCore;
    private readonly BeltGenerator beltGenerator;
    private readonly GameLevelSchema.BoardVisualReferences activeBoardReferences;

    public BoosterManager(GameManager manager, DamplingGameCore core, BeltGenerator belt, GameLevelSchema.BoardVisualReferences boardRefs)
    {
        gameManager = manager;
        gameCore = core;
        beltGenerator = belt;
        activeBoardReferences = boardRefs;
    }

    public void ExecuteRevive()
    {
        List<int> beltColors = beltGenerator.GetBeltsColors();

        var topColors = beltColors.GroupBy(c => c)
                                  .OrderByDescending(g => g.Count())
                                  .Select(g => new { ColorIndex = g.Key, Count = g.Count() })
                                  .Take(2)
                                  .ToList();

        if (topColors.Count == 0) return;

        Vector2Int[] spawnCoords = { new Vector2Int(3, -1), new Vector2Int(4, -1) };

        for (int i = 0; i < topColors.Count; i++)
        {
            int color = topColors[i].ColorIndex;
            int ballsToExtract = Mathf.Min(9, topColors[i].Count);

            beltGenerator.ExtractBallsByColor(color, ballsToExtract);

            var newNode = gameCore.InjectReviveUnit(spawnCoords[i].x, spawnCoords[i].y, color, ballsToExtract);

            Vector3 spawnPosition = i == 0 ? new Vector2(-0.3f, -0.424f) : new Vector2(0.3f, -0.424f);

            GameObject unitInstance = DamplingObjectPool.Instance.GetUnit(spawnPosition, Quaternion.identity, gameManager.transform);
            UnitView newUnitView = unitInstance.GetComponent<UnitView>();

            newUnitView.Initialize(newNode);
            activeBoardReferences.UnitViews[spawnCoords[i]] = newUnitView;
        }

        Debug.Log("Revive Executed! New units spawned at Row -1.");

        beltGenerator.ResumeBelt();
    }

    public void ExecuteMagnet(UnitView targetedUnitView)
    {
        if (targetedUnitView == null || activeBoardReferences == null) return;

        var unitData = gameCore.FindUnitById(targetedUnitView.UnitId);
        if (unitData == null || gameCore.PlayedUnitIds.Contains(unitData.UnitId)) return;

        gameCore.PlayedUnitIds.Add(unitData.UnitId);
        var node = gameCore.FindCellNodeByUnitId(unitData.UnitId);
        if (node != null) node.OccupyingUnit = null;

        int ballsSent = 0;
        int totalBalls = unitData.InteriorContents.Count;

        foreach (var dumpling in unitData.InteriorContents)
        {
            int targetColor = dumpling.ColorIndex;

            var matchingContainers = activeBoardReferences.ContainerViews.Values
                .Where(v => v != null && v.gameObject.activeInHierarchy && v.CurrentRequiredColorIndex == targetColor)
                .OrderBy(v => v.transform.position.y);

            foreach (var container in matchingContainers)
            {
                if (container.TryReserveTargetSlot(out Transform targetSlot))
                {
                    bool isContainerNowFull = container.IsContainerFullyBooked();

                    targetedUnitView.FlyBallToTarget(targetSlot, () =>
                    {
                        ballsSent++;

                        if (isContainerNowFull)
                        {
                            container.gameObject.SetActive(false);
                            gameManager.AdvanceContainerQueue(container.QueueIndex, container);
                        }

                        if (ballsSent == totalBalls)
                        {
                            targetedUnitView.gameObject.SetActive(false);
                            gameManager.EvaluateLogicalWinState();
                        }
                    });
                    break;
                }
            }
        }
    }

    public void ExecuteShuffle()
    {
        if (activeBoardReferences == null || activeBoardReferences.ContainerViews == null) return;

        var activeQueues = activeBoardReferences.ContainerViews.Values
            .Where(v => v != null && v.gameObject.activeInHierarchy)
            .GroupBy(v => v.QueueIndex)
            .ToList();

        List<ContainerView> row1 = new List<ContainerView>();
        List<ContainerView> row2 = new List<ContainerView>();

        foreach (var queue in activeQueues)
        {
            var orderedColumn = queue.OrderBy(v => v.transform.position.y).ToList();

            if (orderedColumn.Count > 0) row1.Add(orderedColumn[0]);
            if (orderedColumn.Count > 1) row2.Add(orderedColumn[1]);
        }

        if (row1.Count == 0 || row1.Count != row2.Count)
        {
            Debug.Log("Shuffle Aborted: The amount of containers in Row 1 and Row 2 do not match.");
            return;
        }

        for (int i = 0; i < row1.Count; i++)
        {
            var r1Container = row1[i];
            var r2Container = row2[i];

            ToggleColliders(r1Container, false);
            ToggleColliders(r2Container, false);

            Vector3 r1LogicalPos = activeBoardReferences.logicalContainerPositions.ContainsKey(r1Container)
                ? activeBoardReferences.logicalContainerPositions[r1Container]
                : r1Container.transform.position;

            Vector3 r2LogicalPos = activeBoardReferences.logicalContainerPositions.ContainsKey(r2Container)
                ? activeBoardReferences.logicalContainerPositions[r2Container]
                : r2Container.transform.position;

            activeBoardReferences.logicalContainerPositions[r1Container] = r2LogicalPos;
            activeBoardReferences.logicalContainerPositions[r2Container] = r1LogicalPos;

            r1Container.transform.DOKill();
            r2Container.transform.DOKill();

            Sequence swapSequence = DOTween.Sequence();

            Vector3 liftPosition = r1LogicalPos + new Vector3(0, 0.5f, 0);
            swapSequence.Append(r1Container.transform.DOMove(liftPosition, 0.15f).SetEase(Ease.OutQuad).OnUpdate(r1Container.SyncSeatedBalls));
            swapSequence.Append(r2Container.transform.DOMove(r1LogicalPos, 0.25f).SetEase(Ease.InOutSine).OnUpdate(r2Container.SyncSeatedBalls));
            swapSequence.Append(r1Container.transform.DOMove(r2LogicalPos, 0.2f).SetEase(Ease.InQuad).OnUpdate(r1Container.SyncSeatedBalls));

            swapSequence.OnComplete(() =>
            {
                ToggleColliders(r1Container, true);
                ToggleColliders(r2Container, true);
                r1Container.SyncSeatedBalls();
                r2Container.SyncSeatedBalls();
            });
        }

        Debug.Log("Shuffle Executed! Front two rows matched and swapped.");
    }

    private void ToggleColliders(ContainerView containerView, bool state)
    {
        if (containerView != null)
        {
            containerView.DisableEnableCollider(state);
        }
    }
}