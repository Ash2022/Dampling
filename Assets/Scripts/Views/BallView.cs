using UnityEngine;
using DG.Tweening;
using System;

public class BallView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private Collider2D col;

    private SlotView currentSlotView;

    public int ColorIndex { get; private set; }
    public SpriteRenderer SR => spriteRenderer;
    public Collider2D Collider => col;

    public void Initialize(int colorIndex)
    {
        spriteRenderer.color = Color.white;
        ColorIndex = colorIndex;

        currentSlotView?.Release();
        currentSlotView = null;

        spriteRenderer.sprite = VisualsManager.Instance.GetBallSprite(colorIndex);
        spriteRenderer.sortingOrder = 30;

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        transform.DOKill();
        col.enabled = false;
    }

    public void ActivatePhysicsSim()
    {
        rb.bodyType = RigidbodyType2D.Dynamic;
        col.enabled = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (currentSlotView == null && other.CompareTag("Slot"))
        {
            SlotView slotView = other.GetComponent<SlotView>();
            if (slotView.TryClaim(this))
            {
                currentSlotView = slotView;

                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;

                transform.SetParent(slotView.transform);
                transform.DOKill();
                transform.DOLocalMove(Vector3.zero, 0.15f).SetEase(Ease.OutQuad);

                GameManager.Instance.BallEnteredSlot(this);
                GameManager.Instance.BallEnteredOrExitSlot();
            }
        }


        if (currentSlotView != null && other.CompareTag("Container"))
        {
            ContainerView container = other.GetComponent<ContainerView>();
            if (container.CurrentRequiredColorIndex == ColorIndex)
            {
                if (container.TryReserveTargetSlot(out Transform targetSlotTransform))
                {
                    ExecuteTransferToContainer(container, targetSlotTransform);
                    GameManager.Instance.BallEnteredOrExitSlot();
                }
            }
        }
    }

    public void ExecuteTransferToContainer(ContainerView targetContainer, Transform destinationSlot)
    {
        currentSlotView.Release();
        currentSlotView = null;
        col.enabled = false;

        transform.SetParent(destinationSlot);
        transform.DOKill();

        SoundsManager.Instance.BallJumpToContainer();

        transform.DOLocalRotate(Vector3.zero, 0.25f).SetEase(Ease.InOutSine);
        transform.DOLocalMove(Vector3.zero, 0.25f).SetEase(Ease.InOutSine).OnComplete(() =>
        {
            targetContainer.OnBallAbsorbed(this);
        });
    }

    internal void MoveHigher()
    {
        spriteRenderer.sortingOrder = 36;
    }

    internal void ExecuteWinkVisual()
    {
        SoundsManager.Instance.WinkHappen();
    }

    internal void ClearSlotReference()
    {
        currentSlotView = null;
    }
}