using UnityEngine;
using System.Linq;
using static GameLevelSchema;

public class UnitView : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    
    private Vector2Int gridCoordinate;

    public void Initialize(CellNode cellNode)
    {
        gridCoordinate = new Vector2Int(cellNode.Position.X, cellNode.Position.Y);

        // Fetch lock state cleanly from GameManager
        bool isLocked = GameManager.Instance.IsUnitLockedAt(gridCoordinate);

        if (cellNode.ContinuousPipe != null)
        {
            var firstUnit = cellNode.ContinuousPipe.ReservoirQueue.FirstOrDefault();
            string pipeColorId = firstUnit?.InteriorContents.FirstOrDefault()?.ColorId ?? "";
            
            // Pipes are structural entries; color is always visible if content exists
            spriteRenderer.color = DamplingGameUtils.GetColorById(pipeColorId);
        }
        else if (cellNode.OccupyingUnit != null)
        {
            // Evaluate hidden vs revealed color rules
            bool isHidden = cellNode.OccupyingUnit.IsHiddenUntilUnblocked;
            string unitColorId = isHidden ? "Hidden" : (cellNode.OccupyingUnit.InteriorContents.FirstOrDefault()?.ColorId ?? "");
            
            Color unitColor = DamplingGameUtils.GetColorById(unitColorId);

            // Apply clear visual tint factor strictly if locked, regardless of hidden state
            spriteRenderer.color = isLocked ? unitColor * 0.4f : unitColor;
        }
        else
        {
            spriteRenderer.color = DamplingGameUtils.GetColorById("");
        }
    }

    public void OnViewClicked()
    {
        // Guard click immediately if GameManager registers this coordinate space as locked
        if (GameManager.Instance.IsUnitLockedAt(gridCoordinate)) return;

        GameManager.Instance.OnUnitElementClicked(gridCoordinate);
    }
}