using UnityEngine;

public class BallView : MonoBehaviour
{
    private Rigidbody2D rb;
    private bool isCaptured;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        Collider2D col = GetComponent<Collider2D>();
        Debug.Log($"Ball is valid: {rb != null && col != null && col.isTrigger == false}");
    }


    void OnTriggerEnter2D(Collider2D other)
    {
        Debug.Log("T");

        if (!isCaptured && other.CompareTag("Slot"))
        {
            if (other.transform.childCount == 0)
            {
                isCaptured = true;
                rb.isKinematic = true;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;

                transform.SetParent(other.transform);
                transform.localPosition = Vector3.zero;
            }
        }
    }
}