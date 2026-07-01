using UnityEngine;
using DG.Tweening;

public class BallView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Rigidbody2D rb;
    private bool isCaptured;
    private Transform currentBeltSlot;

    public string ColorId { get; private set; }
    public SpriteRenderer SR => spriteRenderer;


    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public void Initialize(string colorId)
    {
        ColorId = colorId;
        isCaptured = false;
        currentBeltSlot = null;
        spriteRenderer.color = DamplingGameUtils.GetColorById(colorId);

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        transform.DOComplete();
    }

    public void ActivatePhysicsSim()
    {
        if (rb != null && !isCaptured)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        //Debug.Log("TriggerEnter");

        // 1. BELT CONVEYOR SLOT INTERCEPTION
        if (!isCaptured && other.CompareTag("Slot"))
        {
            if (other.transform.childCount == 0)
            {
                isCaptured = true;
                currentBeltSlot = other.transform;

                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;

                transform.SetParent(currentBeltSlot);
                transform.localPosition = Vector3.zero;
            }
        }

        // 2. SCROLLING CONTAINER MATCH RESOLUTION HANDSHAKE
        if (isCaptured && currentBeltSlot != null && other.CompareTag("Container"))
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
        currentBeltSlot = null;
        transform.SetParent(null);

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