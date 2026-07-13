using UnityEngine;
using System.Collections.Generic;
using DG.Tweening;
using static GameLevelSchema;

public class ContainerView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Transform[] localBallTargetSlots; // Assign 3 local attachment transforms in inspector

    [SerializeField] private Collider2D contCollider;


    public int QueueIndex { get; set; }

    private List<BallView> absorbedBallViews = new List<BallView>();
    private ContainerData dataModel;
    private int reservedSlotsCount = 0; // Guard variable to prevent double-claiming on the same frame



    public int CurrentRequiredColorIndex => dataModel != null ? dataModel.ColorIndex : -1;
    public ContainerData Model => dataModel;

    public void Initialize(ContainerData containerData, int orgQueueIndex)
    {
        dataModel = containerData;
        reservedSlotsCount = dataModel.FilledSlotsCount; // Synchronize with data layer state
        QueueIndex = orgQueueIndex;
        absorbedBallViews.Clear();

        spriteRenderer.sprite = VisualsManager.Instance.GetContainerSprite(containerData.ColorIndex);

        // Reset visual alphas/scales back to normal defaults when pulled from pool
        spriteRenderer.DOComplete();
        spriteRenderer.color = new Color(spriteRenderer.color.r, spriteRenderer.color.g, spriteRenderer.color.b, 1f);
        transform.localScale = Vector3.one;
    }

    /// <summary>
    /// Checks if the container can accept a ball and reserves a visual slot atomically.
    /// </summary>
    public bool TryReserveTargetSlot(out Transform targetSlotTransform)
    {
        targetSlotTransform = null;

        if (dataModel == null || reservedSlotsCount >= dataModel.Capacity)
            return false;

        // Use local attachment references, fallback to math offsets if slots aren't manually assigned
        if (localBallTargetSlots != null && reservedSlotsCount < localBallTargetSlots.Length)
        {
            targetSlotTransform = localBallTargetSlots[reservedSlotsCount];
        }
        else
        {
            // This fallback is problematic as it doesn't provide a transform to follow.
            // This indicates a setup issue. For now, we will fail the reservation.
            Debug.LogError($"Container '{name}' is missing a reference for localBallTargetSlots[{reservedSlotsCount}]. Cannot reserve a slot.", gameObject);
            return false;
        }

        reservedSlotsCount++;
        return true;
    }

    /// <summary>
    /// Atomically updates the raw backend data model layer state once the ball physically lands.
    /// </summary>
    public void OnBallAbsorbed(BallView ballView)
    {
        if (dataModel == null || ballView == null) return;

        // Track the component directly—no runtime lookups needed
        absorbedBallViews.Add(ballView);
        dataModel.FilledSlotsCount++;

        transform.DOPunchScale(new Vector3(0.15f, 0.15f, 0f), 0.15f, 10, 1f);

        if (dataModel.FilledSlotsCount >= dataModel.Capacity)
        {
            ExecuteFulfillmentSequence();
        }
    }

    public bool IsContainerFullyBooked()
    {
        return reservedSlotsCount >= dataModel.Capacity;
    }

    public bool HasRoomLeft()
    {
        return dataModel != null && dataModel.FilledSlotsCount < dataModel.Capacity;
    }

    private void ExecuteFulfillmentSequence()
    {
        Sequence clearSeq = DOTween.Sequence();
        clearSeq.Append(transform.DOScale(Vector3.zero, 0.25f).SetEase(Ease.InBack));
        clearSeq.Join(spriteRenderer.DOFade(0f, 0.25f));

        // Smoothly fade out the nested balls using the pre-cached view component
        foreach (var ballView in absorbedBallViews)
        {
            if (ballView != null)
            {
                // Direct access bypasses GetComponent completely
                clearSeq.Join(ballView.SR.DOFade(0f, 0.25f));
            }
        }

        clearSeq.OnComplete(() =>
        {
            // Bulk recycle directly via the GameObject accessor on the component
            foreach (var ballView in absorbedBallViews)
            {
                if (ballView != null) DamplingObjectPool.Instance.ReturnBall(ballView.gameObject);
            }
            absorbedBallViews.Clear();

            // NOTIFY MANAGER: Tell GameManager exactly which column index needs to advance
            GameManager.Instance.AdvanceContainerQueue(QueueIndex, this);

            DamplingObjectPool.Instance.ReturnContainer(gameObject);
        });
        clearSeq.Play();
    }

    public void DisableEnableCollider(bool colState)
    {
        contCollider.enabled = colState;
    }



    // This forces the unparented balls to perfectly stick to the container's slots
    public void SyncSeatedBalls()
    {
        for (int i = 0; i < absorbedBallViews.Count; i++)
        {
            // Ensure the ball exists and we have a corresponding target slot
            if (absorbedBallViews[i] != null && localBallTargetSlots != null && i < localBallTargetSlots.Length)
            {
                absorbedBallViews[i].transform.position = localBallTargetSlots[i].position;
            }
        }
    }
}