using System.Collections.Generic;
using UnityEngine;

public class MicroAgitationVolume : MonoBehaviour
{
    public float agitationDelay = 0.25f;
    public float agitationForce = 1.5f;

    private Dictionary<Rigidbody2D, float> trackedBodies = new Dictionary<Rigidbody2D, float>();

    private void OnTriggerEnter2D(Collider2D other)
    {
        trackedBodies[other.attachedRigidbody] = Time.time;
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        Rigidbody2D rb = other.attachedRigidbody;
        
        if (Time.time - trackedBodies[rb] > agitationDelay)
        {
            Vector2 randomJitter = new Vector2(Random.Range(-1f, 1f), Random.Range(0.2f, 1f)).normalized * agitationForce;
            rb.AddForce(randomJitter, ForceMode2D.Impulse);
            Debug.Log("BallPushedByVolume");
            
            trackedBodies[rb] = Time.time;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        trackedBodies.Remove(other.attachedRigidbody);
    }

    public void Reset()
    {
        trackedBodies.Clear();    
    }
}