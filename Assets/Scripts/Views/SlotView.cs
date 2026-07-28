using UnityEngine;

public class SlotView : MonoBehaviour
{
    public BallView OccupyingBall { get; private set; }
    public bool IsOccupied => OccupyingBall != null;

    [SerializeField] private SpriteRenderer slotSprite;
    public SpriteRenderer SR => slotSprite;

    public bool TryClaim(BallView ball)
    {
        if (IsOccupied) return false;
        
        OccupyingBall = ball;
        return true;
    }

    public void Release() => OccupyingBall = null;
}