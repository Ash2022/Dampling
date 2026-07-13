using UnityEngine;
using TMPro;

public class FPSCounter2 : MonoBehaviour
{
    [Header("UI Element to Display FPS")]
    public TMP_Text fpsText;  // If using TextMeshPro, use TMP_Text

    // The moving average "smoothness": smaller factor -> more smoothing
    [SerializeField, Range(0f, 1f)]
    private float smoothingFactor = 0.1f;

    private float deltaTime = 0f;

    void Update()
    {
        // Exponential moving average of deltaTime
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * smoothingFactor;

        // Convert deltaTime to FPS
        float fps = 1f / deltaTime;

        // Update UI
        if (fpsText != null)
        {
            fpsText.text = fps.ToString("F2");
        }
    }
}