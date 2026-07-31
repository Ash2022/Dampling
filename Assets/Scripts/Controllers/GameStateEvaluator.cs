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

        if (activeBoardReferences == null || activeBoardReferences.ContainerQueues == null)
            return resolvableColors;

        foreach (var queue in activeBoardReferences.ContainerQueues)
        {
            if (queue.Count > 0 && queue[0] != null && queue[0].gameObject.activeInHierarchy && !queue[0].IsResolved)
            {
                resolvableColors.Add(queue[0].CurrentRequiredColorIndex);
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
            //Debug.Log("Visual Deadlock! The belt is full and no items match the active containers.");
            return true;
        }

        return false;
    }

    public bool CheckForLogicalWin()
    {

        //Debug.Log("Check Logic Win");

        int currentBallsOnBelt = beltGenerator.GetBeltsColors().Count;
        int emptyBeltSlots = GameManager.BELT_CAPACITY - currentBallsOnBelt;

        return gameManager.BallsInStagingArea <= emptyBeltSlots;
    }

    public bool CheckForVisualWin()
    {
        if (activeBoardReferences == null || activeBoardReferences.ContainerQueues == null) return false;

        foreach (var queue in activeBoardReferences.ContainerQueues)
        {
            if (queue.Count > 0)
            {
                return false;
            }
        }

        return true;
    }
}