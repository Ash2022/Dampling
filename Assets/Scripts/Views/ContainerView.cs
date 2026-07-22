using UnityEngine;
using System.Collections.Generic;
using DG.Tweening;
using static GameLevelSchema;
using System;

public class ContainerView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Transform[] localBallTargetSlots; // Assign 3 local attachment transforms in inspector

    [SerializeField] private Collider2D contCollider;

    public SpriteRenderer SR => spriteRenderer;
    public string contName;
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

        if(containerData.startHidden)
            spriteRenderer.sprite = VisualsManager.Instance.GetContainerSprite(-1);
        else
            spriteRenderer.sprite = VisualsManager.Instance.GetContainerSprite(containerData.ColorIndex);

        // Reset visual alphas/scales back to normal defaults when pulled from pool
        spriteRenderer.DOComplete();
        spriteRenderer.color = new Color(spriteRenderer.color.r, spriteRenderer.color.g, spriteRenderer.color.b, 1f);
        transform.localScale = Vector3.one;
        spriteRenderer.sortingOrder = 1;
    }

    public void RevealContainerColor()
    {
        if(dataModel.startHidden)
        {
            dataModel.startHidden = false;
            spriteRenderer.sprite = VisualsManager.Instance.GetContainerSprite(dataModel.ColorIndex);
        }

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
            Debug.LogError($"Container '{contName}' is missing a reference for localBallTargetSlots[{reservedSlotsCount}]. Cannot reserve a slot.", gameObject);
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

        transform.DOKill(true);
        transform.localScale = Vector3.one;
        transform.DOPunchScale(new Vector3(0.15f, 0.15f, 0f), 0.1f, 10, 1f);

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
        // Ensure absorb animation is halted before fulfillment starts
        transform.DOKill(true);

        Sequence clearSeq = DOTween.Sequence();

        // --- CONFIGURATION PARAMS ---
        float animDuration = 0.25f;
        float upwardTravelDistance = 0.5f;

        spriteRenderer.sortingOrder = 2;

        // Phase 1: Move upwards first
        clearSeq.Append(transform.DOMoveY(transform.position.y + upwardTravelDistance, animDuration).SetEase(Ease.InSine).OnUpdate(() =>
        {
            SyncSeatedBalls();
        }).OnComplete(() =>
        {
            // Spawn the resolution effect at the container's final position before it is recycled
            DamplingObjectPool.Instance.GetContainerResolveEffect(transform.position, Quaternion.identity);
        }));

        // Phase 2: Scale down and fade out concurrently after the upward motion completes
        clearSeq.Append(transform.DOScale(Vector3.zero, animDuration).SetEase(Ease.InSine));
        clearSeq.Join(spriteRenderer.DOFade(0f, animDuration));

        foreach (var ballView in absorbedBallViews)
        {
            clearSeq.Join(ballView.SR.DOFade(0f, animDuration));
        }

        clearSeq.OnComplete(() =>
        {
            foreach (var ballView in absorbedBallViews)
            {
                DamplingObjectPool.Instance.ReturnBall(ballView.gameObject);
            }
            absorbedBallViews.Clear();



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

    public Transform GetNextAvailableSlotTransform()
    {
        if (localBallTargetSlots == null || reservedSlotsCount >= localBallTargetSlots.Length)
            return null;

        Transform slot = localBallTargetSlots[reservedSlotsCount];
        reservedSlotsCount++;
        return slot;
    }
}