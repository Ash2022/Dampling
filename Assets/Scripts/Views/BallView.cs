using UnityEngine;

public class BallView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;

    private Rigidbody2D rb;
    private bool isCaptured;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    /// <summary>
    /// Locks the ball down instantly so physics gravity won't drag it out of the unit box layout at startup.
    /// </summary>
    public void Initialize(string colorId)
    {
        isCaptured = false;
        spriteRenderer.color = DamplingGameUtils.GetColorById(colorId);

        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic; // Lock gravity calculations completely
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    /// <summary>
    /// Invoked directly from the UnitView DOTween timeline when a ball finishes its jump arc to fall into the funnel.
    /// </summary>
    public void ActivatePhysicsSim()
    {
        if (rb != null && !isCaptured)
        {
            rb.bodyType = RigidbodyType2D.Dynamic; // Let gravity and collisions resume naturally
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!isCaptured && other.CompareTag("Slot"))
        {
            if (other.transform.childCount == 0)
            {
                isCaptured = true;
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;

                transform.SetParent(other.transform);
                transform.localPosition = Vector3.zero;
            }
        }
    }
}