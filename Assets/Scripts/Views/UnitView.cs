using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using DG.Tweening;
using static GameLevelSchema;
using UnityEngine.EventSystems;
using System;

public class UnitView : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private SpriteRenderer lidRenderer; // Reference to the round box lid

    [SerializeField] public SpriteRenderer lockOverlayRenderer; // Padlock/Chain overlay graphic
    [SerializeField] private SpriteRenderer keyIndicatorRenderer; // Decorative key badge icon

    [SerializeField] private SpriteRenderer pipeTextDisplay; // Decorative key badge icon


    [SerializeField] private LineRenderer linkLineRenderer;

    [Header("Ice Overlay Features")]
    [SerializeField] private GameObject iceOverlayRenderer; // Assign in Inspector
    [SerializeField] private TMPro.TextMeshPro iceCountText;

    [SerializeField] private Transform clickIndication;

    Transform ballsOrgParent;

    private Vector2Int gridCoordinate;
    private List<BallView> preAllocatedBallViews = new List<BallView>();
    private Sequence releaseSequence;
    private Sequence openLidSequence;
    public int unitColorIndex = -1;

    private bool wasInitiallyHidden = false;

    bool disableButton = false;

    public int UnitId { get; private set; }
    public void Initialize(GameLevelSchema.CellNode cellNode)
    {
        gridCoordinate = new Vector2Int(cellNode.Position.X, cellNode.Position.Y);
        CleanUpActiveSequence();
        ReturnBallsToPool();

        // Reset relation overlays, lines, and text layers to safely handle recycling
        disableButton = false;
        clickIndication.gameObject.SetActive(false);
        lockOverlayRenderer.gameObject.SetActive(false);
        keyIndicatorRenderer.gameObject.SetActive(false);
        iceOverlayRenderer.gameObject.SetActive(false);
        pipeTextDisplay.gameObject.SetActive(false);
        linkLineRenderer.positionCount = 0;

        // 1. Process Static Pipe Generation Matrix Cells
        if (cellNode.ContinuousPipe != null)
        {
            UnitId = -1; // Pipes are anchor locations, not standard unit structures

            var firstUnit = cellNode.ContinuousPipe.ReservoirQueue.FirstOrDefault();
            unitColorIndex = firstUnit?.InteriorContents.FirstOrDefault()?.ColorIndex ?? -1;

            spriteRenderer.sprite = VisualsManager.Instance.GetPipeSprite();

            lidRenderer.gameObject.SetActive(false);

            int emissionsLeft = cellNode.ContinuousPipe.MaxTotalEmissions ?? 3;

            pipeTextDisplay.sprite = VisualsManager.Instance.GetPipeCounterSprite(emissionsLeft);
            pipeTextDisplay.gameObject.SetActive(true);

            disableButton = true;
        }
        // 2. Process Standard Playable Unit Grid Cells
        else if (cellNode.OccupyingUnit != null)
        {
            UnitId = cellNode.OccupyingUnit.UnitId;

            // --- 1. HIDDEN STATE ---
            bool isHidden = cellNode.OccupyingUnit.IsHiddenUntilUnblocked;
            wasInitiallyHidden = isHidden; // Save the flag!

            unitColorIndex = isHidden ? -1 : (cellNode.OccupyingUnit.InteriorContents.FirstOrDefault()?.ColorIndex ?? -1);

            //Color unitColor = DamplingGameUtils.GetColorByIndex(unitColorIndex);

            spriteRenderer.sprite = VisualsManager.Instance.GetUnitSprite(unitColorIndex);
            lidRenderer.sprite = VisualsManager.Instance.GetUnitLidSprite(unitColorIndex);

            //lidRenderer.color = unitColor;
            lidRenderer.gameObject.SetActive(true);

            if (isHidden)
            {
                
            }
            else if (cellNode.OccupyingUnit.InteriorContents.Count > 0)
            {
                SetupNestedInteriorBalls(cellNode.OccupyingUnit.InteriorContents);
            }

            // --- 2. LOCK / KEY STATE ---
            // The lock overlay is ONLY for explicit dependencies!
            if (cellNode.OccupyingUnit.ExplicitlyBlockedByUnitIds.Count > 0)
            {
                lockOverlayRenderer.gameObject.SetActive(true);
            }

            bool isAKeyUnit = GameManager.Instance.IsUnitActingAsKey(cellNode.OccupyingUnit.UnitId);
            if (isAKeyUnit)
            {
                keyIndicatorRenderer.gameObject.SetActive(true);
            }

            // --- 3. ICE STATE ---
            int iceLayers = cellNode.OccupyingUnit.IceLayers;
            if (iceLayers > 0)
            {
                iceOverlayRenderer.gameObject.SetActive(true);

                if (iceCountText != null)
                {
                    iceCountText.text = iceLayers.ToString();
                }

                //spriteRenderer.color = Color.Lerp(unitColor, new Color(0.5f, 0.8f, 1f), 0.6f);
                //lidRenderer.color = Color.Lerp(unitColor, new Color(0.5f, 0.8f, 1f), 0.6f);
            }

            if (cellNode.Position.Y <= 0)
                RemoveLidCover();

        }
        // 3. Process Empty / Carved Out Boundary Gaps / Blocked Cells
        else
        {
            UnitId = -1;
            //spriteRenderer.color = DamplingGameUtils.GetColorByIndex(-1);
            lidRenderer.gameObject.SetActive(false);
        }
    }
    public void RenderLinkLines(UnitView partnerView)
    {
        if (partnerView == null || linkLineRenderer == null) return;

        // Configure and draw the line directly between the two verified transforms
        linkLineRenderer.positionCount = 2;
        linkLineRenderer.SetPosition(0, this.transform.position);
        linkLineRenderer.SetPosition(1, partnerView.transform.position);
    }


    private void SetupNestedInteriorBalls(List<GameLevelSchema.DumplingItem> contents)
    {
        float radius = 0.125f;
        Vector3 centerPos = transform.position;

        // 1. PRE-PROCESS STAGE: Compute original mathematical positions accurately
        Vector3[] rawMathPositions = new Vector3[9];
        rawMathPositions[0] = centerPos;
        for (int i = 1; i < 9; i++)
        {
            float angle = (i - 1) * (360f / 8f) * Mathf.Deg2Rad;
            Vector3 worldOffset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            rawMathPositions[i] = centerPos + worldOffset + new Vector3(0f, 0.02f, 0f);
        }

        // 2. REVERSED MAPPING STAGE: Exactly inverted to flip the visual layering order
        int[] visualToMathMap = new int[] { 6, 7, 8, 5, 0, 1, 4, 3, 2 };

        // 3. CREATION & DATA STAGE: Sequential instantiation builds reversed hierarchy depth
        for (int i = 0; i < contents.Count && i < 9; i++)
        {
            int mathIndex = visualToMathMap[i];
            Vector3 targetWorldPos = rawMathPositions[mathIndex];

            GameObject ball = DamplingObjectPool.Instance.GetBall(targetWorldPos, Quaternion.identity);
            ball.transform.localScale = Vector3.one * 0.45f;

            BallView bView = ball.GetComponent<BallView>();
            bView.Initialize(contents[i].ColorIndex);
            GameManager.Instance.ballViews.Add(bView);

            preAllocatedBallViews.Add(bView);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OnViewClicked();
    }

    public void OnViewClicked()
    {
        if (GameManager.Instance.IsGameOver())
        {
            Debug.Log("Game is over");
            return;
        }

        if (GameManager.Instance.IsMagnet())
        {
            GameManager.Instance.UseMagnetBooster(this);
            disableButton = true;
            return;
        }

        if (GameManager.Instance.IsUnitLockedAt(gridCoordinate))
        {
            Debug.Log("unit blocked");
            return;
        }

        if (disableButton)
            return;

        disableButton = true;

        //check if we are in magnet mode - 


        GameManager.Instance.OnUnitElementClicked(gridCoordinate);
        ExecuteReleaseUnitContents();

    }

    public void LinkedUnitPlayed()
    {
        ExecuteReleaseUnitContents();
    }

    private void ExecuteReleaseUnitContents()
    {
        if (releaseSequence != null && releaseSequence.IsActive())
            releaseSequence.Kill();

        float uniformFinalScale = 1f;
        float giantOvershootMultiplier = 6.5f;
        float normalOvershootMultiplier = 1.1f;
        float giantChance = 0.05f;
        float blastDelayStagger = 0.05f;

        // Tweens only control the elastic scale phase now
        float scaleAnimDuration = 0.2f;
        float scaleUpDuration = scaleAnimDuration * 0.3f;
        float scaleDownDuration = scaleAnimDuration * 0.7f;

        releaseSequence = DOTween.Sequence();
        int totalBalls = preAllocatedBallViews.Count - 1;

        for (int i = 0; i < preAllocatedBallViews.Count; i++)
        {
            BallView ballView = preAllocatedBallViews[totalBalls - i];
            Transform ballTransform = ballView.transform;

            float jumpDelay = i * blastDelayStagger;

            bool isGiantPop = UnityEngine.Random.value < giantChance;
            float chosenMultiplier = isGiantPop ? giantOvershootMultiplier : normalOvershootMultiplier;

            Vector3 peakOvershootScale = Vector3.one * (uniformFinalScale * chosenMultiplier);
            Vector3 settledFinalScale = Vector3.one * uniformFinalScale;

            // 1. Instantly hand off to physics at the start of this ball's timeline
            releaseSequence.InsertCallback(jumpDelay, () =>
            {
                ballView.MoveHigher();
                if (isGiantPop) ballView.ExecuteWinkVisual();

                ballView.ActivatePhysicsSim();

                Rigidbody2D rb = ballView.GetComponent<Rigidbody2D>();
                float horizontalSpread = UnityEngine.Random.Range(-0.04f, 0.04f);

                // Adjust this multiplier based on your Rigidbody2D mass gravity scale
                float upwardForce = UnityEngine.Random.Range(0.5f, 0.65f);
                rb.AddForce(new Vector2(horizontalSpread, upwardForce), ForceMode2D.Impulse);
            });

            float activeScaleUp = isGiantPop ? scaleUpDuration * 7f : scaleUpDuration;
            float activeScaleDown = isGiantPop ? scaleDownDuration * 7f : scaleDownDuration;

            // 2. Run the visual scale tween on top of the physics trajectory
            releaseSequence.Insert(jumpDelay, ballTransform.DOScale(peakOvershootScale, activeScaleUp).SetEase(Ease.OutQuad));
            releaseSequence.Insert(jumpDelay + scaleUpDuration, ballTransform.DOScale(settledFinalScale, activeScaleDown).SetEase(Ease.InOutSine));

            GameManager.Instance.BallsInStagingArea++;
        }

        float finalBallPopTime = totalBalls * blastDelayStagger;
        float fadeOutStart = finalBallPopTime + 0.3f;
        releaseSequence.Insert(fadeOutStart, spriteRenderer.DOFade(0f, 0.5f));

        releaseSequence.OnComplete(() =>
        {
            preAllocatedBallViews.Clear();
            DamplingObjectPool.Instance.ReturnUnit(gameObject);
        });

        releaseSequence.Play();
    }

    private void ReturnBallsToPool()
    {
        foreach (var ballView in preAllocatedBallViews)
        {
            if (ballView != null) DamplingObjectPool.Instance.ReturnBall(ballView.gameObject);
        }
        preAllocatedBallViews.Clear();
    }

    private void CleanUpActiveSequence()
    {
        if (releaseSequence != null && releaseSequence.IsActive())
            releaseSequence.Kill();

        if (openLidSequence != null && openLidSequence.IsActive())
            openLidSequence.Kill();

    }

    public void UpdatePipeCounter(int newPipeCount)
    {
        if(newPipeCount ==0)
            pipeTextDisplay.gameObject.SetActive(false);
        else
            pipeTextDisplay.sprite = VisualsManager.Instance.GetPipeCounterSprite(newPipeCount);
    }

    /// <summary>
    /// Incremental update called when nearby tiles explode, reducing the freeze counter.
    /// </summary>
    public void UpdateIceLayers(int remainingLayers, Color originalUnitColor)
    {
        if (iceCountText != null && remainingLayers > 0)
        {
            iceCountText.text = remainingLayers.ToString();
        }

        // Optional: Fade the frosty blue tint slightly as ice gets thinner
        // spriteRenderer.color = Color.Lerp(originalUnitColor, new Color(0.5f, 0.8f, 1f), remainingLayers * 0.2f);
    }

    /// <summary>
    /// Instantly thaws the unit, hiding visual blocks and restoring natural sprite coloring.
    /// </summary>
    public void ShatterIce(Color originalUnitColor)
    {
        if (iceOverlayRenderer != null)
        {
            iceOverlayRenderer.gameObject.SetActive(false);
        }

        // Restore the unit's natural coloring
        spriteRenderer.color = originalUnitColor;
        lidRenderer.color = originalUnitColor;
    }

    public void LockUnlocked()
    {
        lockOverlayRenderer.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        CleanUpActiveSequence();
    }



    internal void UnitBecameUnBlocked(GameLevelSchema.CellNode updatedNode)
    {
        // We only need to do a visual overhaul if the unit was hiding its true identity
        if (wasInitiallyHidden)
        {
            wasInitiallyHidden = false;
            
            pipeTextDisplay.gameObject.SetActive(false);
        
            // 1. Fetch the true color now that it is revealed
            unitColorIndex = updatedNode.OccupyingUnit.InteriorContents.FirstOrDefault()?.ColorIndex ?? -1;
            //Color realColor = DamplingGameUtils.GetColorByIndex(unitColorIndex);

            spriteRenderer.sprite = VisualsManager.Instance.GetUnitSprite(unitColorIndex);
            lidRenderer.sprite = VisualsManager.Instance.GetUnitLidSprite(unitColorIndex);

            // 3. Spawn the true contents inside it
            if (updatedNode.OccupyingUnit.InteriorContents.Count > 0)
            {
                SetupNestedInteriorBalls(updatedNode.OccupyingUnit.InteriorContents);
            }

        }

        // If it wasn't hidden, we do nothing here! 
        // The lock turning off is handled safely by your HandleLockKeyCollected callback.

        RemoveLidCover();

    }

    public void RemoveLidCover()
    {
        if (openLidSequence.IsActive())
            openLidSequence.Kill();

        openLidSequence = DOTween.Sequence();

        float duration = 0.4f;
        Vector3 targetFlyPosition = transform.position + new Vector3(0.6f, 1.2f, 0f);

        // Initial subtle high-speed squish down on Y axis and stretch on X axis to show build-up force
        openLidSequence.Append(lidRenderer.transform.DOScale(new Vector3(1.2f, 0.4f, 1f), duration * 0.25f).SetEase(Ease.OutQuad));

        // Pop up, scale thin during flight, rotate rapidly, and fade out cleanly
        openLidSequence.Append(lidRenderer.transform.DOMove(targetFlyPosition, duration * 0.75f).SetEase(Ease.OutCubic));
        openLidSequence.Join(lidRenderer.transform.DOScale(new Vector3(0.8f, 0.2f, 1f), duration * 0.75f).SetEase(Ease.InQuad));
        openLidSequence.Join(lidRenderer.transform.DORotate(new Vector3(0f, 0f, 180f), duration * 0.75f, RotateMode.FastBeyond360).SetEase(Ease.OutQuad));
        openLidSequence.Join(lidRenderer.DOFade(0f, duration * 0.75f).SetEase(Ease.InQuint));

        openLidSequence.OnComplete(() =>
        {
            lidRenderer.gameObject.SetActive(false);
        });

        openLidSequence.Play();
    }

    public void FlyBallToTargetExtended(Vector3 targetPosition, float delay, Action<BallView> onComplete)
    {
        float animTime = 1f;

        var ball = preAllocatedBallViews[0];
        preAllocatedBallViews.RemoveAt(0);

        Sequence flySeq = DOTween.Sequence();

        if (delay > 0f)
        {
            flySeq.AppendInterval(delay);
        }

        flySeq.AppendCallback(() =>
        {
            ball.transform.SetParent(null);
            ball.SR.sortingOrder = 36;
        });

        flySeq.Append(ball.transform.DOScale(Vector3.one, animTime / 2f).SetEase(Ease.OutQuad));
        flySeq.Join(ball.transform.DOMove(targetPosition, animTime).SetEase(Ease.InSine));

        flySeq.OnComplete(() =>
        {
            onComplete.Invoke(ball);
        });

        flySeq.Play();
    }

    public void FadeOutBox()
    {
        spriteRenderer.DOFade(0f, 0.5f).OnComplete(() =>
        {
            preAllocatedBallViews.Clear();
            DamplingObjectPool.Instance.ReturnUnit(gameObject);
        });
    }

    public void ShowHideClickIndication(bool showHide)
    {
        clickIndication.gameObject.SetActive(showHide);
    }

    internal bool IsLidOn()
    {
        return lidRenderer.gameObject.activeInHierarchy;
    }

    internal void PipeInitialize(GameLevelSchema.CellNode cellNode)
    {
        unitColorIndex = cellNode.OccupyingUnit.InteriorContents.FirstOrDefault().ColorIndex ;

        spriteRenderer.sprite = VisualsManager.Instance.GetUnitSprite(unitColorIndex);
        lidRenderer.sprite = VisualsManager.Instance.GetUnitLidSprite(unitColorIndex);

        //lidRenderer.color = unitColor;
        lidRenderer.gameObject.SetActive(true);
        disableButton = true;
        clickIndication.gameObject.SetActive(false);
        lockOverlayRenderer.gameObject.SetActive(false);
        keyIndicatorRenderer.gameObject.SetActive(false);
        iceOverlayRenderer.gameObject.SetActive(false);
        pipeTextDisplay.gameObject.SetActive(false);
        linkLineRenderer.positionCount = 0;
        
    }
}