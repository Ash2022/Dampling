using UnityEngine;
using DG.Tweening;

public class BallView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Rigidbody2D rb;
    private Collider2D col;
    private bool isCaptured;
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
        // If captured, snap to the slot's position each frame. This avoids parenting.
        if (isCaptured && currentBeltSlot != null)
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

                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;

                // Position will be snapped in the next LateUpdate.
            }
        }

        // 2. SCROLLING CONTAINER MATCH RESOLUTION HANDSHAKE
        if (isCaptured && other.CompareTag("Container"))
        {
            ContainerView container = other.GetComponent<ContainerView>();
            if (container != null && container.CurrentRequiredColorId == ColorId)
            {
                // Request slot target reservation space atomically
                if (container.TryReserveTargetSlot(out Vector3 targetWorldPosition))
                {
                    ExecuteTransferToContainer(container, targetWorldPosition);
                }
            }
        }
    }

    private void ExecuteTransferToContainer(ContainerView targetContainer, Vector3 destinationWorldPos)
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

        Sequence jumpSeq = DOTween.Sequence();

        // Jump arc up into the targeted visual slot inside the container column
        jumpSeq.Append(transform.DOMove(destinationWorldPos, 0.4f).SetEase(Ease.InOutSine));

        // Update atomic model tracking states
        jumpSeq.OnComplete(() =>
        {
            if (targetContainer != null)
            {
                // Pass the direct BallView component reference straight to the container
                targetContainer.OnBallAbsorbed(this);
            }
            else
            {
                DamplingObjectPool.Instance.ReturnBall(gameObject);
            }
        });

        jumpSeq.Play();
    }
}