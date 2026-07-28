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

    [SerializeField]MicroAgitationVolume microAgitationVolume;

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
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasContainer, screenPoint, Camera.main, out var localPoint);

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

        microAgitationVolume.Reset();
        PlayerData data = ModelManager.Instance.Data;

        foreach (var view in boosterButtons)
        {
            bool isUnlocked = false;
            int currentCount = 0;
            int unlocksAtLevel = 0;

            if (view.Type == BoosterButtonView.BoosterType.Magnet)
            {
                isUnlocked = currentLevelIndex >= ModelManager.MAGNET_UNLOCKED;
                unlocksAtLevel = ModelManager.MAGNET_UNLOCKED;
                currentCount = data.MagnetBoosterCount;
            }
            else if (view.Type == BoosterButtonView.BoosterType.Shuffle)
            {
                isUnlocked = currentLevelIndex >= ModelManager.SHUFFLE_UNLOCKED;
                unlocksAtLevel = ModelManager.SHUFFLE_UNLOCKED;
                currentCount = data.ShuffleBoosterCount;
            }

            view.Setup(isUnlocked, currentCount, HandleBoosterClick, unlocksAtLevel);
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
                SoundsManager.Instance.BoosterClicked(true);
                ToggleAllUnitsIndication(true);
                gameManager.MagnetClicked();
            }
            else if (gameManager.currentState == GameManager.GameState.Magnet)
            {
                SoundsManager.Instance.BoosterClicked(false);
                ToggleAllUnitsIndication(false);
                gameManager.MagnetClicked();
            }
        }
        else if (type == BoosterButtonView.BoosterType.Shuffle)
        {
            if (data.ShuffleBoosterCount <= 0) return;

            SoundsManager.Instance.BoosterClicked(true);
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
            //not hidden units
            if (unitView.isMagnetBlocked == false)
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

        float xPosWorld = 0.33792f;
        float yPosWorld = -0.32416f;

        for (int i = 0; i < topColors.Count; i++)
        {
            int color = topColors[i].ColorIndex;
            int ballsToExtract = Mathf.Min(9, topColors[i].Count);

            beltGenerator.ExtractBallsByColor(color, ballsToExtract);

            var newNode = gameCore.InjectReviveUnit(spawnCoords[i].x, spawnCoords[i].y, color, ballsToExtract);

            Vector3 spawnPosition = i == 0 ? new Vector2(-xPosWorld, yPosWorld) : new Vector2(xPosWorld, yPosWorld);

            GameObject unitInstance = DamplingObjectPool.Instance.GetUnit(spawnPosition, Quaternion.identity, gameManager.transform);
            UnitView newUnitView = unitInstance.GetComponent<UnitView>();

            GameManager.Instance.NotifyLevelVisualizerAboutNewUnits(newUnitView);

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

            ContainerView targetContainer = null;
            Transform targetSlot = null;

            int maxDepth = 0;
            foreach (var q in activeBoardReferences.ContainerQueues)
            {
                if (q.Count > maxDepth) maxDepth = q.Count;
            }

            for (int depth = 0; depth < maxDepth; depth++)
            {
                foreach (var queue in activeBoardReferences.ContainerQueues)
                {
                    if (depth < queue.Count)
                    {
                        var container = queue[depth];
                        if (container != null && container.gameObject.activeInHierarchy &&
                            container.CurrentRequiredColorIndex == targetColor &&
                            !container.IsContainerFullyBooked() && !container.IsResolved)
                        {
                            targetSlot = container.GetNextAvailableSlotTransform();
                            if (targetSlot != null)
                            {
                                targetContainer = container;
                                break;
                            }
                        }
                    }
                }
                if (targetContainer != null) break;
            }

            
            float currentDelay = baseDelay + (i * staggerInterval);
            var capturedContainer = targetContainer;
            var capturedSlot = targetSlot;

            targetedUnitView.FlyBallToTargetExtended(capturedSlot.position, currentDelay, (ballView) =>
            {
                ballView.transform.SetParent(capturedContainer.transform);
                capturedContainer.OnBallAbsorbed(ballView);

                completedBallsFlight++;

                if (completedBallsFlight == totalBalls)
                {
                    targetedUnitView.FadeOutBox();
                }
            });
        }

        gameManager.EvaluateLogicalWinState();
    }

    public void ExecuteShuffle(float speedMultiplier = 3f)
    {
        if (activeBoardReferences == null || activeBoardReferences.ContainerQueues == null) return;

        foreach (var queue in activeBoardReferences.ContainerQueues)
        {
            if (queue == null || queue.Count < 2) continue;

            var r1Container = queue[0];
            var r2Container = queue[1];

            if (r1Container == null || r2Container == null || r1Container.IsResolved || r2Container.IsResolved) continue;

            r1Container.DisableEnableCollider(false);
            r2Container.DisableEnableCollider(false);

            r1Container.SR.sortingOrder = 2;

            Vector3 r1Pos = r1Container.transform.position;
            Vector3 r2Pos = r2Container.transform.position;

            queue[0] = r2Container;
            queue[1] = r1Container;

            r1Container.transform.DOKill();
            r2Container.transform.DOKill();

            Sequence swapSequence = DOTween.Sequence();
            float animDuration = 0.4f * speedMultiplier;

            swapSequence.Append(r1Container.transform.DOJump(r2Pos, 0.75f, 1, animDuration).SetEase(Ease.InOutQuad));
            swapSequence.Join(r2Container.transform.DOMove(r1Pos, animDuration).SetEase(Ease.InOutQuad));

            swapSequence.OnComplete(() =>
            {
                r1Container.DisableEnableCollider(true);
                r2Container.DisableEnableCollider(true);

                r1Container.SR.sortingOrder = 1;

                r2Container.RevealContainerColor();
            });
        }
    }

}