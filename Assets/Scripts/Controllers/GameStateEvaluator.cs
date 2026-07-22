using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameStateEvaluator
{
    // Dependencies
    private readonly BeltGenerator beltGenerator;
    private readonly GameLevelSchema.BoardVisualReferences activeBoardReferences;
    private readonly GameManager gameManager;

    public GameStateEvaluator(GameManager manager, BeltGenerator belt, GameLevelSchema.BoardVisualReferences boardRefs)
    {
        gameManager = manager;
        beltGenerator = belt;
        activeBoardReferences = boardRefs;
    }

    /// <summary>
    /// Scans the actual UI elements to find which containers are currently at the front of the line.
    /// </summary>
    private List<int> GetVisualAvailableContainerColors()
    {
        List<int> resolvableColors = new List<int>();

        if (activeBoardReferences == null || activeBoardReferences.ContainerViews == null)
            return resolvableColors;

        var unresolvedContainers = activeBoardReferences.ContainerViews.Values
            .Where(v => v != null && v.gameObject.activeInHierarchy && v.HasRoomLeft());

        var visualQueues = unresolvedContainers.GroupBy(v => v.QueueIndex);

        foreach (var queueGroup in visualQueues)
        {
            var headContainer = queueGroup.OrderBy(v => v.transform.position.y).FirstOrDefault();

            if (headContainer != null)
            {
                resolvableColors.Add(headContainer.Model.ColorIndex);
            }
        }

        return resolvableColors.Distinct().ToList();
    }

    public bool CheckForVisualDeadlock()
    {
        bool slotsFull = beltGenerator.AllSlotsFull();
        if (!slotsFull) return false;

        List<int> beltColors = beltGenerator.GetBeltsColors();
        List<int> activeContainerColors = GetVisualAvailableContainerColors();

        bool matchPossible = beltColors.Any(color => activeContainerColors.Contains(color));

        if (!matchPossible)
        {
            Debug.Log("Visual Deadlock! The belt is full and no items match the active containers.");
            return true;
        }

        return false;
    }

    public bool CheckForLogicalWin()
    {
        if (activeBoardReferences == null || activeBoardReferences.UnitViews == null) return false;

        if (activeBoardReferences.UnitViews.Values.Any(unit => unit.gameObject.activeInHierarchy 
        && unit.ModelData.ContinuousPipe ==null))
        {
            return false;
        }

        //Debug.Log("Check Logic Win");

        int currentBallsOnBelt = beltGenerator.GetBeltsColors().Count;
        int emptyBeltSlots = GameManager.BELT_CAPACITY - currentBallsOnBelt;

        return gameManager.BallsInStagingArea <= emptyBeltSlots;
    }

    public bool CheckForVisualWin()
    {
        if (activeBoardReferences == null || activeBoardReferences.ContainerViews == null) return false;

        foreach (var container in activeBoardReferences.ContainerViews.Values)
        {
            if (container != null && container.gameObject.activeInHierarchy && container.HasRoomLeft())
            {
                return false;
            }
        }

        return true;
    }
}