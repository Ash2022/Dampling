using UnityEngine;
using System.Collections.Generic;
using static ModelManager;
using System.Linq;
using DG.Tweening;

public class BoosterManager : MonoBehaviour
{
    [Header("World Space Target Anchors")]
    [SerializeField] private Transform magnetWorldTarget;
    [SerializeField] private Transform shuffleWorldTarget;

    [Header("UI Canvas Layout Configuration")]
    [SerializeField] private RectTransform canvasContainer;
    [SerializeField] private List<BoosterButtonView> boosterButtons;

    // Persistent Architecture Dependencies
    private GameManager gameManager;
    private BeltGenerator beltGenerator;

    // Dynamic Runtime Level Dependencies
    private DamplingGameCore gameCore;


    private GameLevelSchema.BoardVisualReferences activeBoardReferences;

    private Dictionary<BoosterButtonView.BoosterType, BoosterButtonView> buttonViewsMap = new Dictionary<BoosterButtonView.BoosterType, BoosterButtonView>();

    /// <summary>
    /// Invoked exactly once upon scene/manager loading. 
    /// Handles persistent dependency binding and absolute UI anchoring calculations.
    /// </summary>
    public void Initialize(GameManager manager, BeltGenerator belt, UIManager uIManager)
    {
        gameManager = manager;
        beltGenerator = belt;

        buttonViewsMap.Clear();
        foreach (var view in boosterButtons)
        {
            buttonViewsMap[view.Type] = view;

            Transform targetTransform = view.Type == BoosterButtonView.BoosterType.Magnet ? magnetWorldTarget : shuffleWorldTarget;

            // 1) Translate 3D World Space to 2D Screen Pixel Space
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(Camera.main, targetTransform.position);

            // 2) Convert Screen Pixel Space directly into local Canvas space (null camera for Overlay)
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasContainer, screenPoint, null, out var localPoint);

            view.Rect.anchoredPosition = localPoint;
        }
    }

    /// <summary>
    /// Invoked on every level transition. 
    /// Refreshes unlock constraints, operational states, and active level data context.
    /// </summary>
    public void InitLevel(DamplingGameCore core, GameLevelSchema.BoardVisualReferences boardRefs, int currentLevelIndex)
    {
        gameCore = core;
        activeBoardReferences = boardRefs;

        PlayerData data = ModelManager.Instance.Data;

        foreach (var view in boosterButtons)
        {
            bool isUnlocked = false;
            int currentCount = 0;

            if (view.Type == BoosterButtonView.BoosterType.Magnet)
            {
                isUnlocked = currentLevelIndex >= ModelManager.MAGNET_BOOSTER;
                currentCount = data.MagnetBoosterCount;
            }
            else if (view.Type == BoosterButtonView.BoosterType.Shuffle)
            {
                isUnlocked = currentLevelIndex >= ModelManager.SHUFFLE_BOOSTER;
                currentCount = data.ShuffleBoosterCount;
            }

            view.Setup(isUnlocked, currentCount, HandleBoosterClick);
        }
    }

    private void HandleBoosterClick(BoosterButtonView.BoosterType type)
    {
        PlayerData data = ModelManager.Instance.Data;

        if (type == BoosterButtonView.BoosterType.Magnet)
        {
            if (data.MagnetBoosterCount <= 0) return;

            if (gameManager.currentState == GameManager.GameState.ReadyToPlay)
            {
                ToggleAllUnitsIndication(true);
                gameManager.MagnetClicked();
            }
            else if (gameManager.currentState == GameManager.GameState.Magnet)
            {
                ToggleAllUnitsIndication(false);
                gameManager.MagnetClicked();
            }
        }
        else if (type == BoosterButtonView.BoosterType.Shuffle)
        {
            if (data.ShuffleBoosterCount <= 0) return;

            // Instant Execution
            ExecuteShuffle();
            data.ShuffleBoosterCount--;
            ModelManager.Instance.SaveData();
            RefreshButtonVisuals(type);
        }
    }

    private void ToggleAllUnitsIndication(bool show)
    {
        foreach (var unitView in activeBoardReferences.UnitViews.Values)
        {
            unitView.ShowHideClickIndication(show);
        }
    }

    public void RefreshButtonVisuals(BoosterButtonView.BoosterType type)
    {
        BoosterButtonView view = buttonViewsMap[type];
        PlayerData data = ModelManager.Instance.Data;

        int currentCount = type == BoosterButtonView.BoosterType.Magnet ? data.MagnetBoosterCount : data.ShuffleBoosterCount;
        view.Setup(true, currentCount, HandleBoosterClick);
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
        ToggleAllUnitsIndication(false);

        ModelManager.Instance.AdjustMagnetCount(-1);
        RefreshButtonVisuals(BoosterButtonView.BoosterType.Magnet);

        var unitData = gameCore.FindUnitById(targetedUnitView.UnitId);
        gameCore.PlayedUnitIds.Add(unitData.UnitId);
        var node = gameCore.FindCellNodeByUnitId(unitData.UnitId);
        node.OccupyingUnit = null;

        int totalBalls = unitData.InteriorContents.Count;

        bool hasLid = targetedUnitView.IsLidOn();
        if (hasLid)
        {
            targetedUnitView.RemoveLidCover();
        }

        float baseDelay = hasLid ? 0.35f : 0f;
        float staggerInterval = 0.15f;
        int completedBallsFlight = 0;


        for (int i = 0; i < totalBalls; i++)
        {
            var dumpling = unitData.InteriorContents[i];
            int targetColor = dumpling.ColorIndex;

            // Added !v.IsContainerFullyBooked() to bypass containers that reached capacity
            var targetContainer = activeBoardReferences.ContainerViews.Values
    .Where(v => v.gameObject.activeInHierarchy &&
                v.CurrentRequiredColorIndex == targetColor &&
                !v.IsContainerFullyBooked())
    .OrderBy(v => v.transform.position.y)
    .FirstOrDefault();

            float currentDelay = baseDelay + (i * staggerInterval);

            Vector3 targetPosition = targetContainer.GetNextAvailableSlotTransform().position;

            targetedUnitView.FlyBallToTargetExtended(targetPosition, currentDelay, (ballView) =>
            {
                targetContainer.OnBallAbsorbed(ballView);

                completedBallsFlight++;

                if (completedBallsFlight == totalBalls)
                {
                    targetedUnitView.FadeOutBox();
                }

            });
        }

        //targetedUnitView.gameObject.SetActive(false);
        gameManager.EvaluateLogicalWinState();
    }

    public void ExecuteShuffle(float speedMultiplier = 3f)
    {
        var activeQueues = activeBoardReferences.ContainerViews.Values
            .Where(v => v.gameObject.activeInHierarchy)
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

        if (row1.Count == 0 || row1.Count != row2.Count) return;

        for (int i = 0; i < row1.Count; i++)
        {
            var r1Container = row1[i];
            var r2Container = row2[i];

            ToggleColliders(r1Container, false);
            ToggleColliders(r2Container, false);

            r1Container.SR.sortingOrder = 2;

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
            float animDuration = 0.4f * speedMultiplier;

            swapSequence.Append(r1Container.transform.DOJump(r2LogicalPos, 0.75f, 1, animDuration)
                .SetEase(Ease.InOutQuad)
                .OnUpdate(r1Container.SyncSeatedBalls));

            swapSequence.Join(r2Container.transform.DOMove(r1LogicalPos, animDuration)
                .SetEase(Ease.InOutQuad)
                .OnUpdate(r2Container.SyncSeatedBalls));

            swapSequence.OnComplete(() =>
            {
                ToggleColliders(r1Container, true);
                ToggleColliders(r2Container, true);

                r1Container.SR.sortingOrder = 1;

                r1Container.SyncSeatedBalls();
                r2Container.SyncSeatedBalls();

                r2Container.RevealContainerColor();
            });
        }
    }

    public void ExecuteSkipLevel()
    {
        var activeBalls = GameManager.Instance.ballViews.ToList();
        int totalBalls = activeBalls.Count;

        if (totalBalls == 0)
        {
            gameManager.EvaluateLogicalWinState();
            return;
        }

        for (int i = 0; i < totalBalls; i++)
        {
            var ballView = activeBalls[i];
            
            var targetContainer = activeBoardReferences.ContainerViews.Values
                .Where(v => v.CurrentRequiredColorIndex == ballView.ColorIndex)
                .OrderByDescending(v => v.gameObject.activeInHierarchy)
                .ThenBy(v => v.QueueIndex)
                .First(v => v.HasRoomLeft());

            targetContainer.TryReserveTargetSlot(out Transform targetSlot);


            ballView.GetComponent<Collider2D>().enabled = false;
            ballView.transform.DOKill();

            GameManager.Instance.BallEnteredOrExitSlot();

            DOVirtual.DelayedCall(i * 0.05f, () =>
            {
                ballView.ExecuteTransferToContainer(targetContainer, targetSlot);
            });
        }

        DOVirtual.DelayedCall((totalBalls * 0.05f) + 0.5f, () => gameManager.EvaluateLogicalWinState());
    }

    private void ToggleColliders(ContainerView containerView, bool state)
    {
        if (containerView != null)
        {
            containerView.DisableEnableCollider(state);
        }
    }
}