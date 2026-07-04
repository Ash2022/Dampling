using UnityEngine;
using TMPro;

public class BlockerCellView : MonoBehaviour
{
    [Header("Visual Variant Anchors")]
    [SerializeField] private GameObject permanentWallGraphics;
    [SerializeField] private GameObject crateGraphics;

    [Header("Crate UI Elements")]
    [SerializeField] private TextMeshPro durabilityText;


    public Vector2Int GridCoordinate { get; private set; }

    /// <summary>
    /// Configures the pooled cell asset layout dynamically based on core matrix state
    /// </summary>
    public void Initialize(Vector2Int coordinate, int crateDurability)
    {
        GridCoordinate = coordinate;

        if (crateDurability == -1)
        {
            // Configure as a permanent structural wall/hole
            permanentWallGraphics.SetActive(true);
            crateGraphics.SetActive(false);
            if (durabilityText != null) durabilityText.gameObject.SetActive(false);
        }
        else if (crateDurability > 0)
        {
            // Configure as a destructible obstacle crate
            permanentWallGraphics.SetActive(false);
            crateGraphics.SetActive(true);
            
            if (durabilityText != null)
            {
                durabilityText.gameObject.SetActive(true);
                durabilityText.text = crateDurability.ToString();
            }
        }
    }

    /// <summary>
    /// Updates the text value display or triggers damage feedback animations
    /// </summary>
    public void UpdateDurability(int remainingDurability)
    {
        if (durabilityText != null && remainingDurability > 0)
        {
            durabilityText.text = remainingDurability.ToString();
        }
        
        // Optional: Trigger a slight scale pop/shake juice animation here on hit
    }

    /// <summary>
    /// Executes destruction visual effects before recycling back into the pool
    /// </summary>
    public void PlayShatterAndClear()
    {
        // Return to pool management architecture via your existing workflow
        gameObject.SetActive(false);
    }
}