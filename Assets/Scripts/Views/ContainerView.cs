using UnityEngine;
using System.Collections.Generic;
using DG.Tweening;
using static GameLevelSchema;

public class ContainerView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Transform[] localBallTargetSlots;
    [SerializeField] private Collider2D contCollider;

    public SpriteRenderer SR => spriteRenderer;
    public string contName;
    public int QueueIndex { get; set; }

    private List<BallView> absorbedBallViews = new List<BallView>();
    private ContainerData dataModel;
    private int reservedSlotsCount = 0;

    public GameObject containerResolveEffect;

    public int CurrentRequiredColorIndex => dataModel.ColorIndex;
    public ContainerData Model => dataModel;
    public bool IsResolved { get; private set; }

    public void Initialize(ContainerData containerData, int orgQueueIndex)
    {
        dataModel = containerData;
        reservedSlotsCount = dataModel.FilledSlotsCount;
        QueueIndex = orgQueueIndex;
        absorbedBallViews.Clear();
        containerResolveEffect = null;
        
        IsResolved = false;

        if (containerData.startHidden)
            spriteRenderer.sprite = VisualsManager.Instance.GetContainerSprite(-1);
        else
            spriteRenderer.sprite = VisualsManager.Instance.GetContainerSprite(containerData.ColorIndex);

        spriteRenderer.DOComplete();
        spriteRenderer.color = new Color(spriteRenderer.color.r, spriteRenderer.color.g, spriteRenderer.color.b, 1f);
        transform.localScale = Vector3.one;
        spriteRenderer.sortingOrder = 1;
    }

    public void RevealContainerColor()
    {
        if (dataModel.startHidden)
        {
            dataModel.startHidden = false;
            spriteRenderer.sprite = VisualsManager.Instance.GetContainerSprite(dataModel.ColorIndex);
        }
    }

    public bool TryReserveTargetSlot(out Transform targetSlotTransform)
    {
        targetSlotTransform = null;

        if (reservedSlotsCount >= dataModel.Capacity)
            return false;

        targetSlotTransform = localBallTargetSlots[reservedSlotsCount];
        reservedSlotsCount++;
        return true;
    }

    public void OnBallAbsorbed(BallView ballView)
    {
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
        return dataModel.FilledSlotsCount < dataModel.Capacity;
    }

    private void ExecuteFulfillmentSequence()
    {
        transform.DOKill(true);
        IsResolved = true;
        
        Sequence clearSeq = DOTween.Sequence();
        float animDuration = 0.25f;
        float upwardTravelDistance = 0.5f;

        spriteRenderer.sortingOrder = 2;

        clearSeq.Append(transform.DOMoveY(transform.position.y + upwardTravelDistance, animDuration).SetEase(Ease.InSine).OnComplete(() =>
        {
            SoundsManager.Instance.ContainerResolved();
            containerResolveEffect = DamplingObjectPool.Instance.GetContainerResolveEffect(transform.position, Quaternion.identity);
        }));

        clearSeq.Append(transform.DOScale(Vector3.zero, animDuration).SetEase(Ease.InSine));
        clearSeq.Join(spriteRenderer.DOFade(0f, animDuration));

        foreach (var ballView in absorbedBallViews)
        {
            clearSeq.Join(ballView.SR.DOFade(0f, animDuration));
        }

        clearSeq.OnComplete(() =>
        {
            DamplingObjectPool.Instance.ReturnContainerResolveEffect(containerResolveEffect);
            GameManager.Instance.AdvanceContainerQueue(QueueIndex, this);
        });
    }

    public void DisableEnableCollider(bool colState)
    {
        contCollider.enabled = colState;
    }

    public Transform GetNextAvailableSlotTransform()
    {
        if (reservedSlotsCount >= localBallTargetSlots.Length)
            return null;

        Transform slot = localBallTargetSlots[reservedSlotsCount];
        reservedSlotsCount++;
        return slot;
    }
}