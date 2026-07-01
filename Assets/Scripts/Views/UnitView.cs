using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using DG.Tweening;
using static GameLevelSchema;
using UnityEngine.EventSystems;

public class UnitView : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private SpriteRenderer lidRenderer; // Reference to the round box lid

    private Vector2Int gridCoordinate;
    private List<GameObject> preAllocatedBalls = new List<GameObject>();
    private Sequence resolveSequence;
    private string unitColorId;

    public void Initialize(CellNode cellNode)
    {
        gridCoordinate = new Vector2Int(cellNode.Position.X, cellNode.Position.Y);
        CleanUpActiveSequence();
        ReturnBallsToPool();

        bool isLocked = GameManager.Instance.IsUnitLockedAt(gridCoordinate);

        if (cellNode.ContinuousPipe != null)
        {
            var firstUnit = cellNode.ContinuousPipe.ReservoirQueue.FirstOrDefault();
            unitColorId = firstUnit?.InteriorContents.FirstOrDefault()?.ColorId ?? "";
            spriteRenderer.color = DamplingGameUtils.GetColorById(unitColorId);
            lidRenderer.color = DamplingGameUtils.GetColorById(unitColorId);
            lidRenderer.gameObject.SetActive(true);
        }
        else if (cellNode.OccupyingUnit != null)
        {
            bool isHidden = cellNode.OccupyingUnit.IsHiddenUntilUnblocked;
            unitColorId = isHidden ? "Hidden" : (cellNode.OccupyingUnit.InteriorContents.FirstOrDefault()?.ColorId ?? "");

            Color unitColor = DamplingGameUtils.GetColorById(unitColorId);
            spriteRenderer.color = unitColor;
            lidRenderer.color = unitColor;
            lidRenderer.gameObject.SetActive(true);

            // Pre-allocate and arrange the 9 balls sitting silently scaled down inside the box layout
            if (!isHidden && cellNode.OccupyingUnit.InteriorContents.Count > 0)
            {
                SetupNestedInteriorBalls(cellNode.OccupyingUnit.InteriorContents);
            }
        }
        else
        {
            spriteRenderer.color = DamplingGameUtils.GetColorById("");
            lidRenderer.gameObject.SetActive(false);
        }
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
            if (bView != null) bView.Initialize(contents[i].ColorId);

            preAllocatedBalls.Add(ball);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {

        Debug.Log("Mouse Down");

        OnViewClicked();
    }

    public void OnViewClicked()
    {
        if (GameManager.Instance.IsUnitLockedAt(gridCoordinate)) return;

        GameManager.Instance.OnUnitElementClicked(gridCoordinate);
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
        for (int i = 0; i < preAllocatedBalls.Count; i++)
        {
            GameObject ball = preAllocatedBalls[i];
            float jumpDelay = 0.1f + (i * delayBetweenBalls);

            // Jump arc outward while expanding up to native scale
            Vector3 jumpTarget = ball.transform.position + new Vector3(Random.Range(-0.05f, 0.05f), 0.2f, 0f); // Target down toward funnel
            resolveSequence.Insert(jumpDelay, ball.transform.DOJump(jumpTarget, 0.25f, 1, 0.35f).SetEase(Ease.OutQuad));
            resolveSequence.Insert(jumpDelay, ball.transform.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutBack));

            // Switch Rigidbody2D back to Dynamic right as the jump animation lands
            resolveSequence.InsertCallback(jumpDelay + 0.35f, () =>
            {
                if (ball != null)
                {
                    BallView bView = ball.GetComponent<BallView>();
                    if (bView != null) bView.ActivatePhysicsSim();
                }
            });
        }

        // 3. Fade out the main round box container base right after the final ball exits
        float fadeOutStart = 0.1f + (preAllocatedBalls.Count * delayBetweenBalls) + 0.15f;
        resolveSequence.Insert(fadeOutStart, spriteRenderer.DOFade(0f, 0.3f));

        // 4. Return this unit safely to the pool once completely empty and hidden
        resolveSequence.OnComplete(() =>
        {
            preAllocatedBalls.Clear(); // The actual ball instances are now loose processing on the belt
            DamplingObjectPool.Instance.ReturnUnit(gameObject);
        });

        resolveSequence.Play();
    }

    private void ReturnBallsToPool()
    {
        foreach (var ball in preAllocatedBalls)
        {
            if (ball != null) DamplingObjectPool.Instance.ReturnBall(ball);
        }
        preAllocatedBalls.Clear();
    }

    private void CleanUpActiveSequence()
    {
        if (resolveSequence != null && resolveSequence.IsActive())
        {
            resolveSequence.Kill();
        }
    }

    private void OnDestroy()
    {
        CleanUpActiveSequence();
    }
}