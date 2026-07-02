using UnityEngine;

/// </summary>
public class SlotView : MonoBehaviour
{
    public BallView OccupyingBall { get; private set; }
    public bool IsOccupied => OccupyingBall != null;

    /// <summary>
    /// Atomically claims this slot if it is available.
    /// </summary>
    /// <param name="ball">The BallView attempting to claim the slot.</param>
    /// <returns>True if the slot was successfully claimed, false otherwise.</returns>
    public bool TryClaim(BallView ball)
    {
        if (IsOccupied) return false; // Already claimed by another ball.
        
        OccupyingBall = ball;
        return true;
    }

    /// <summary>Marks the slot as available again.</summary>
    public void Release() => OccupyingBall = null;
}

