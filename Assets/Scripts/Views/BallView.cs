using UnityEngine;
using DG.Tweening;
using System;

public class BallView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;

    [SerializeField]private Rigidbody2D rb;
    [SerializeField]private Collider2D col;
    private bool isCaptured;
    private bool isAnimatingCapture;
    private Transform currentBeltSlot;
    private SlotView currentSlotView;

    public int ColorIndex { get; private set; }
    public SpriteRenderer SR => spriteRenderer;
    public Collider2D Collider => col;
    

    public void Initialize(int colorIndex)
    {
        ColorIndex = colorIndex;
        isCaptured = false;
        isAnimatingCapture = false;
        currentBeltSlot = null;
        // If this ball is being recycled while holding a slot, release it.
        if (currentSlotView != null)
        {
            currentSlotView.Release();
        }
        currentSlotView = null;
        
        spriteRenderer.sprite = VisualsManager.Instance.GetBallSprite(colorIndex);
        spriteRenderer.sortingOrder =30;

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        // Kill any active tweens on this object before re-initializing its state.
        transform.DOKill();

        // Ensure the collider is re-enabled when the ball is pulled from the pool.
        col.enabled = false;
    }

    public void ActivatePhysicsSim()
    {
        if (!isCaptured)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            col.enabled = true;
        }
    }

    void LateUpdate()
    {
        // If hooked to a slot (captured AND not animating), snap to its position.
        if (isCaptured && !isAnimatingCapture && currentBeltSlot != null)
        {
            transform.position = currentBeltSlot.position;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        //Debug.Log("TriggerEnter");

        // 1. BELT CONVEYOR SLOT INTERCEPTION
        if (!isCaptured && other.CompareTag("Slot"))
        {
            SlotView slotView = other.GetComponent<SlotView>();
            if (slotView != null && slotView.TryClaim(this))
            {
                isCaptured = true;
                currentBeltSlot = other.transform;
                currentSlotView = slotView;

                rb.bodyType = RigidbodyType2D.Kinematic; rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f;

                isAnimatingCapture = true;
                Vector3 startPos = transform.position;
                transform.DOKill();
                DOVirtual.Float(0f, 1f, 0.15f, t =>
                {
                    if (currentBeltSlot) transform.position = Vector3.Lerp(startPos, currentBeltSlot.position, t);
                }).SetEase(Ease.OutQuad).OnComplete(() => isAnimatingCapture = false);

                GameManager.Instance.BallEnteredSlot(this);
                GameManager.Instance.BallEnteredOrExitSlot();
            }
        }

        // 2. SCROLLING CONTAINER MATCH RESOLUTION HANDSHAKE
        if (isCaptured && other.CompareTag("Container"))
        {
            ContainerView container = other.GetComponent<ContainerView>();
            if (container.CurrentRequiredColorIndex == ColorIndex)
            {
                // Request slot target reservation space atomically
                if (container.TryReserveTargetSlot(out Transform targetSlotTransform))
                {
                    ExecuteTransferToContainer(container, targetSlotTransform);
                    GameManager.Instance.BallEnteredOrExitSlot();
                }
            }
        }
    }

    private void ExecuteTransferToContainer(ContainerView targetContainer, Transform destinationSlot)
    {
        // Sever ties immediately with the conveyor slot infrastructure
        if (currentSlotView != null)
        {
            currentSlotView.Release();
            currentSlotView = null;
        }
        currentBeltSlot = null;
        isCaptured = false; // Stop following the slot in LateUpdate

        // CRITICAL: Disable the collider BEFORE the jump starts to prevent re-triggering other slots.
        col.enabled = false;

        Vector3 startPos = transform.position;
        transform.DOKill();

        // Smoothly interpolate rotation to absolute zero over the exact same flight duration
        transform.DORotate(Vector3.zero, 0.4f).SetEase(Ease.InOutSine);

        DOVirtual.Float(0f, 1f, 0.4f, t =>
        {
            if (destinationSlot) transform.position = Vector3.Lerp(startPos, destinationSlot.position, t);
        }).SetEase(Ease.InOutSine).OnComplete(() =>
        {
            // OnComplete: Snap to final local position and notify container.
            //transform.SetParent(destinationSlot);
            //transform.localPosition = Vector3.zero;
            targetContainer.OnBallAbsorbed(this);
        });
    }

    internal void MoveHigher()
    {
        spriteRenderer.sortingOrder =36;
    }

    internal void ExecuteWinkVisual()
    {
        //Debug.Log("Wink");
    }
}