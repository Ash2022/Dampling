using UnityEngine;
using DG.Tweening;

public class BallView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Rigidbody2D rb;
    private Collider2D col;
    private bool isCaptured;
    private bool isAnimatingCapture;
    private Transform currentBeltSlot;
    private SlotView currentSlotView;

    public string ColorId { get; private set; }
    public SpriteRenderer SR => spriteRenderer;
    public Collider2D Collider => col;


    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
    }

    public void Initialize(string colorId)
    {
        ColorId = colorId;
        isCaptured = false;
        isAnimatingCapture = false;
        currentBeltSlot = null;
        // If this ball is being recycled while holding a slot, release it.
        if (currentSlotView != null)
        {
            currentSlotView.Release();
        }
        currentSlotView = null;
        spriteRenderer.color = DamplingGameUtils.GetColorById(colorId);

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        // Kill any active tweens on this object before re-initializing its state.
        transform.DOKill();

        // Ensure the collider is re-enabled when the ball is pulled from the pool.
        if (col != null) col.enabled = true;
    }

    public void ActivatePhysicsSim()
    {
        if (rb != null && !isCaptured)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
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

                rb.bodyType = RigidbodyType2D.Kinematic; rb.velocity = Vector2.zero; rb.angularVelocity = 0f;

                isAnimatingCapture = true;
                Vector3 startPos = transform.position;
                transform.DOKill();
                DOVirtual.Float(0f, 1f, 0.15f, t =>
                {
                    if (currentBeltSlot) transform.position = Vector3.Lerp(startPos, currentBeltSlot.position, t);
                }).SetEase(Ease.OutQuad).OnComplete(() => isAnimatingCapture = false);
            }
        }

        // 2. SCROLLING CONTAINER MATCH RESOLUTION HANDSHAKE
        if (isCaptured && other.CompareTag("Container"))
        {
            ContainerView container = other.GetComponent<ContainerView>();
            if (container.CurrentRequiredColorId == ColorId)
            {
                // Request slot target reservation space atomically
                if (container.TryReserveTargetSlot(out Transform targetSlotTransform))
                {
                    ExecuteTransferToContainer(container, targetSlotTransform);
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
        if (col != null) col.enabled = false;

        Vector3 startPos = transform.position;
        transform.DOKill();
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
}