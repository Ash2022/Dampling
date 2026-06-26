using UnityEngine;
using UnityEngine.InputSystem;

public class BallSpawner2D : MonoBehaviour
{
    public GameObject ballPrefab;

    void Update()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector3 mousePos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            mousePos.z = 0f; 

            for (int i = 0; i < 10; i++)
                Instantiate(ballPrefab, mousePos, Quaternion.identity);
        }
    }
}