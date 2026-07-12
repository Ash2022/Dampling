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
    [SerializeField] private TMPro.TextMeshPro statusText;
    [SerializeField] private LineRenderer linkLineRenderer;

    [Header("Ice Overlay Features")]
    [SerializeField] private GameObject iceOverlayRenderer; // Assign in Inspector
    [SerializeField] private TMPro.TextMeshPro iceCountText;

    private Vector2Int gridCoordinate;
    private List<BallView> preAllocatedBallViews = new List<BallView>();
    private Sequence resolveSequence;
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
        disableButton= false;
        lockOverlayRenderer.gameObject.SetActive(false);
        keyIndicatorRenderer.gameObject.SetActive(false);
        iceOverlayRenderer.gameObject.SetActive(false);
        linkLineRenderer.positionCount = 0;
        statusText.text = "";

        // 1. Process Static Pipe Generation Matrix Cells
        if (cellNode.ContinuousPipe != null)
        {
            UnitId = -1; // Pipes are anchor locations, not standard unit structures

            var firstUnit = cellNode.ContinuousPipe.ReservoirQueue.FirstOrDefault();
            unitColorIndex = firstUnit?.InteriorContents.FirstOrDefault()?.ColorIndex ?? -1;
            Color pipeColor = DamplingGameUtils.GetColorByIndex(unitColorIndex);

            //override the PIPE color
            spriteRenderer.color = Color.gray;
            lidRenderer.color = Color.gray;
            lidRenderer.gameObject.SetActive(true);

            int emissionsLeft = cellNode.ContinuousPipe.MaxTotalEmissions ?? 3;
            statusText.text = emissionsLeft > 0 ? emissionsLeft.ToString() : "";

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

            Color unitColor = DamplingGameUtils.GetColorByIndex(unitColorIndex);
            spriteRenderer.color = unitColor;
            lidRenderer.color = unitColor;
            lidRenderer.gameObject.SetActive(true);

            if (isHidden)
            {
                statusText.text = "?";
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

                spriteRenderer.color = Color.Lerp(unitColor, new Color(0.5f, 0.8f, 1f), 0.6f);
                lidRenderer.color = Color.Lerp(unitColor, new Color(0.5f, 0.8f, 1f), 0.6f);
            }
        }
        // 3. Process Empty / Carved Out Boundary Gaps / Blocked Cells
        else
        {
            UnitId = -1;
            spriteRenderer.color = DamplingGameUtils.GetColorByIndex(-1);
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
        float radius = 0.1f;
        Vector3 centerPos = transform.position; // Base world position of this unit box

        for (int i = 0; i < contents.Count && i < 9; i++)
        {
            Vector3 targetWorldPos = centerPos;

            if (i > 0)
            {
                // Calculate the 8-around-1 offsets directly in World Space coordinates
                float angle = (i - 1) * (360f / 8f) * Mathf.Deg2Rad;
                Vector3 worldOffset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                targetWorldPos = centerPos + worldOffset;
            }

            // Pull from pool cleanly at its absolute final world position—NO parenting needed
            GameObject ball = DamplingObjectPool.Instance.GetBall(targetWorldPos, Quaternion.identity);
            ball.transform.localScale = Vector3.one * 0.4f;

            BallView bView = ball.GetComponent<BallView>();
            if (bView != null)
            {
                bView.Initialize(contents[i].ColorIndex);
                // CRITICAL FIX: Disable collider on pre-allocated balls to prevent them
                // from interacting with the world while still inside the unit.
                if (bView.Collider != null) bView.Collider.enabled = false;

                GameManager.Instance.ballViews.Add(bView);
            }

            preAllocatedBallViews.Add(bView);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OnViewClicked();
    }

    public void OnViewClicked()
    {
        if (GameManager.Instance.IsUnitLockedAt(gridCoordinate)) return;

        if(disableButton)
            return;


        GameManager.Instance.OnUnitElementClicked(gridCoordinate);
        ExecuteResolveTimeline();
    }

    public void LinkedUnitPlayed()
    {
        ExecuteResolveTimeline();
    }

    private void ExecuteResolveTimeline()
    {
        CleanUpActiveSequence();
        resolveSequence = DOTween.Sequence();

        // 1. Pop the Lid off flying away dynamically
        if (lidRenderer != null)
        {
            resolveSequence.Append(lidRenderer.transform.DOMove(transform.position + new Vector3(1.5f, 3.0f, 0f), 0.4f).SetEase(Ease.OutQuad));
            resolveSequence.Join(lidRenderer.transform.DORotate(new Vector3(0f, 0f, 360f), 0.4f, RotateMode.FastBeyond360));
            resolveSequence.Join(lidRenderer.DOFade(0f, 0.4f));
        }

        // 2. Eject the 9 balls one by one sequentially
        float delayBetweenBalls = 0.05f;
        for (int i = 0; i < preAllocatedBallViews.Count; i++)
        {
            BallView ballView = preAllocatedBallViews[i];
            Transform ballTransform = ballView.transform;
            float jumpDelay = 0.1f + (i * delayBetweenBalls);

            // Jump arc outward while expanding up to native scale
            Vector3 jumpTarget = ballTransform.position + new Vector3(UnityEngine.Random.Range(-0.05f, 0.05f), 0.2f, 0f); // Target down toward funnel
            resolveSequence.Insert(jumpDelay, ballTransform.DOJump(jumpTarget, 0.25f, 1, 0.35f).SetEase(Ease.OutQuad));
            resolveSequence.Insert(jumpDelay, ballTransform.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutBack));

            // Switch Rigidbody2D back to Dynamic right as the jump animation lands
            resolveSequence.InsertCallback(jumpDelay + 0.35f, () =>
            {
                if (ballView != null)
                {
                    // Re-enable the collider just before activating physics.
                    if (ballView.Collider != null) ballView.Collider.enabled = true;
                    ballView.ActivatePhysicsSim();
                }
            });
        }

        // 3. Fade out the main round box container base right after the final ball exits
        float fadeOutStart = 0.1f + (preAllocatedBallViews.Count * delayBetweenBalls) + 0.15f;
        resolveSequence.Insert(fadeOutStart, spriteRenderer.DOFade(0f, 0.3f));

        // 4. Return this unit safely to the pool once completely empty and hidden
        resolveSequence.OnComplete(() =>
        {
            preAllocatedBallViews.Clear(); // The actual ball instances are now loose processing on the belt
            DamplingObjectPool.Instance.ReturnUnit(gameObject);
        });

        resolveSequence.Play();
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
        if (resolveSequence != null && resolveSequence.IsActive())
        {
            resolveSequence.Kill();
        }
    }

    public void UpdatePipeCounter(int newPipeCount)
    {
        statusText.text = newPipeCount > 0 ? newPipeCount.ToString() : "";        
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
            statusText.text = ""; // Remove the "?"

            // 1. Fetch the true color now that it is revealed
            unitColorIndex = updatedNode.OccupyingUnit.InteriorContents.FirstOrDefault()?.ColorIndex ?? -1;
            Color realColor = DamplingGameUtils.GetColorByIndex(unitColorIndex);

            // 2. Apply color (respecting if it happens to still be covered in ice)
            if (updatedNode.OccupyingUnit.IceLayers > 0)
            {
                spriteRenderer.color = Color.Lerp(realColor, new Color(0.5f, 0.8f, 1f), 0.6f);
                lidRenderer.color = Color.Lerp(realColor, new Color(0.5f, 0.8f, 1f), 0.6f);
            }
            else
            {
                spriteRenderer.color = realColor;
                lidRenderer.color = realColor;
            }

            // 3. Spawn the true contents inside it
            if (updatedNode.OccupyingUnit.InteriorContents.Count > 0)
            {
                SetupNestedInteriorBalls(updatedNode.OccupyingUnit.InteriorContents);
            }
        }

        // If it wasn't hidden, we do nothing here! 
        // The lock turning off is handled safely by your HandleLockKeyCollected callback.
    }
}