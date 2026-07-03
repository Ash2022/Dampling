using UnityEngine;
using System.Collections.Generic;
using static GameLevelSchema;
using System.Threading.Tasks;
using System;
using System.Linq;
using DG.Tweening;

public class GameManager : MonoBehaviour
{
    const int BELT_CAPACITY = 28;

    public enum GameState
    {
        Initializing,
        ReadyToPlay,
        ProcessingInput,
        GameEnded
    }

    public static GameManager Instance { get; private set; }

    [SerializeField] private LevelVisualization levelVisualization;
    [SerializeField] private BeltGenerator beltGenerator;

    private DamplingGameCore gameCore;
    private GameLevelSchema currentLevelData;
    private GameState currentState;

    // Persistent progression tracker
    public int CurrentLevelIndex { get; private set; } = 0;

    private BoardVisualReferences activeBoardReferences;

    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private async Task Start()
    {
        await InitializeGame();

        StartLevel(CurrentLevelIndex);
    }

    /// <summary>
    /// Natively executes EXACTLY ONCE at boot-up to set up persistent static subsystems.
    /// </summary>
    public async Task InitializeGame()
    {
        currentState = GameState.Initializing;

        beltGenerator.InitializeBelt(BELT_CAPACITY);

        // Step 1: Await the multi-frame async memory instantiation allocation loop
        await DamplingObjectPool.Instance.InitializePoolsAsync();

        // Step 2: Proceed with standard model loading and data processing now that objects are ready
        ModelManager.Instance.Initialize();

    }

    /// <summary>
    /// Executes dynamically every single time a level starts, reboots, or changes.
    /// </summary>
    public void StartLevel(int levelIndex)
    {
        currentState = GameState.Initializing;
        CurrentLevelIndex = levelIndex;

        // Step 1: Grab target data from model manager via updated progression index
        currentLevelData = ModelManager.Instance.GetLevelByIndex(CurrentLevelIndex);

        // Step 2: Spin up a fresh simulation core instance to clear past game states completely
        gameCore = new DamplingGameCore();
        gameCore.InitializeLevel(currentLevelData);

        // Step 3: Wipe past scene instances and render the fresh board layout array mapping setup
        activeBoardReferences = levelVisualization.RenderInitialBoard(currentLevelData);

        beltGenerator.StartBeltMovement();

        currentState = GameState.ReadyToPlay;
    }

    public bool IsUnitLockedAt(Vector2Int coordinate)
    {
        var cellNode = gameCore.ActiveLevelData.Grid.Matrix.Find(c => c.Position.X == coordinate.x && c.Position.Y == coordinate.y);
        if (cellNode == null || cellNode.OccupyingUnit == null) return false;

        return gameCore.IsUnitBlocked(cellNode.Position, cellNode.OccupyingUnit);
    }

    public void OnUnitElementClicked(Vector2Int coordinate)
    {
        if (currentState != GameState.ReadyToPlay) return;

        currentState = GameState.ProcessingInput;

        List<DamplingGameCore.EngineEvent> transactions = gameCore.ExecutePlayerClick(coordinate.x, coordinate.y);

        foreach (var ev in transactions)
        {
            switch (ev.EventType)
            {
                case DamplingGameCore.EngineEventType.UnitUnblocked:
                    Vector2Int unblockedCoord = (Vector2Int)ev.Payload;
                    if (activeBoardReferences.UnitViews.TryGetValue(unblockedCoord, out var unitView))
                    {
                        var updatedNode = gameCore.ActiveLevelData.Grid.Matrix.Find(c => c.Position.X == unblockedCoord.x && c.Position.Y == unblockedCoord.y);
                        unitView.Initialize(updatedNode);
                    }
                    break;

                case DamplingGameCore.EngineEventType.DumplingMovedToContainer:
                    if (activeBoardReferences.ContainerViews.TryGetValue(ev.TargetId, out var containerView))
                    {
                        // Animate belt transfers here
                    }
                    break;

                case DamplingGameCore.EngineEventType.LevelWon:
                    // Advance progression tracking systematically
                    CurrentLevelIndex++;
                    // In the future, trigger UI panel here before starting next level route
                    //StartLevel(CurrentLevelIndex);
                    return;
            }
        }

        if (gameCore.IsGameOver)
        {
            currentState = GameState.GameEnded;
            return;
        }

        currentState = GameState.ReadyToPlay;
    }

    public void AdvanceContainerQueue(int queueIndex, ContainerView resolvedView)
    {
        // 1. Fixed spacing delta based directly on your layout rules
        float containerSpacingY = 0.3f;

        // 2. Loop directly over your active references tracking dictionary values
        var remainingViewsInColumn = activeBoardReferences.ContainerViews.Values
            .Where(v => v != null && v.QueueIndex == queueIndex && v != resolvedView)
            .ToList();

        // 3. Shift the verified active presentation views down cleanly
        foreach (var container in remainingViewsInColumn)
        {
            Vector3 targetPosition = container.transform.position - new Vector3(0f, containerSpacingY, 0f);

            container.transform.DOKill();
            container.transform.DOMove(targetPosition, 0.3f).SetEase(Ease.OutBack);
        }
    }

    public bool IsUnitActingAsKey(int unitId)
    {
        // Search the active grid matrix matrix to see if any unplayed unit lists this ID as a blocker
        foreach (var cellNode in currentLevelData.Grid.Matrix)
        {
            if (cellNode.OccupyingUnit != null &&
                cellNode.OccupyingUnit.ExplicitlyBlockedByUnitIds.Contains(unitId))
            {
                return true; // Found a lock that depends on this specific unit ID key
            }
        }
        return false;
    }

    public Vector3 GetUnitWorldPositionById(int id)
    {
        // Search directly through the coordinate map you built in the creation loop
        foreach (var unitView in activeBoardReferences.UnitViews.Values)
        {
            if (unitView != null && unitView.UnitId == id)
            {
                return unitView.transform.position;
            }
        }
        return Vector3.zero;
    }
}