using UnityEngine;
using System.Collections.Generic;
using static GameLevelSchema;
using System.Threading.Tasks;
using System;
using System.Linq;
using UnityEngine.InputSystem;
using DG.Tweening;

public class GameManager : MonoBehaviour
{
    public const int BELT_CAPACITY = 30;

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
    public int CurrentLevelIndex = 0;

    private BoardVisualReferences activeBoardReferences;
    public List<BallView> ballViews = new List<BallView>();


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

    private void Update()
    {
        // Cheats for level switching using the new Input System.
        if (Keyboard.current == null) return; // Input system not ready.

        if (Keyboard.current.upArrowKey.wasPressedThisFrame)
        {
            int nextLevelIndex = CurrentLevelIndex + 1;
            if (nextLevelIndex < ModelManager.Instance.LevelCount)
            {
                StartLevel(nextLevelIndex);
            }
            else
            {
                Debug.Log("Already at the last level.");
            }
        }

        if (Keyboard.current.downArrowKey.wasPressedThisFrame)
        {
            int prevLevelIndex = CurrentLevelIndex - 1;
            if (prevLevelIndex >= 0)
            {
                StartLevel(prevLevelIndex);
            }
            else
            {
                Debug.Log("Already at the first level.");
            }
        }
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

        ClearActiveBoard();

        // Step 3: Wipe past scene instances and render the fresh board layout array mapping setup
        activeBoardReferences = levelVisualization.RenderInitialBoard(currentLevelData);

        // Step 2: Spin up a fresh simulation core instance to clear past game states completely
        gameCore = new DamplingGameCore();
        gameCore.InitializeLevel(
            currentLevelData,
            HandleUnitUnblocked,
            HandleUnitIceChanged,
            HandleLockKeyCollected,
            HandleLinkedUnitPlayed
        );

        beltGenerator.StartBeltMovement();

        currentState = GameState.ReadyToPlay;
    }

    public bool IsUnitLockedAt(Vector2Int coordinate)
    {
        var cellNode = gameCore.ActiveLevelData.Grid.Matrix.Find(c => c.Position.X == coordinate.x && c.Position.Y == coordinate.y);
        if (cellNode == null || cellNode.OccupyingUnit == null) return false;

        // 1. GATHER THE CLUSTER (Same logic as in ExecutePlayerClick)
        // You need a helper in DamplingGameCore to get the cluster for a unit ID.
        var clusterIds = gameCore.GetFullClusterIds(cellNode.OccupyingUnit.UnitId);

        // 2. CHECK THE WHOLE CLUSTER
        // Check if ANY unit in that cluster is blocked
        foreach (var id in clusterIds)
        {
            var unit = gameCore.FindUnitById(id);
            var node = gameCore.FindCellNodeByUnitId(id);
            if (gameCore.IsUnitClusterBlocked(node.Position, unit, clusterIds))
            {
                return true; // The UI gatekeeper says "This click is illegal"
            }
        }
        return false;
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
                        unitView.UnitBecameUnBlocked(updatedNode);
                    }
                    break;

                case DamplingGameCore.EngineEventType.DumplingMovedToContainer:
                    if (activeBoardReferences.ContainerViews.TryGetValue(ev.TargetId, out var containerView))
                    {
                        // Animate belt transfers here
                    }
                    break;

                case DamplingGameCore.EngineEventType.PipeEmittedUnit:
                    // 1. UNPACK COMPOSITE COORDINATES
                    var coords = (Tuple<Vector2Int, Vector2Int>)ev.Payload;
                    Vector2Int spawnCoord = coords.Item1;
                    Vector2Int pipeCoord = coords.Item2;

                    Vector3 spawnPosition = Vector3.zero;

                    // 2. CLEANUP CHECK
                    if (activeBoardReferences.UnitViews.TryGetValue(spawnCoord, out var oldView))
                    {
                        if (oldView != null)
                        {
                            spawnPosition = oldView.transform.position;
                            // Return oldView components/balls to pool here if needed
                        }
                    }

                    // 3. SPAWN FROM POOL
                    GameObject unitInstance = DamplingObjectPool.Instance.GetUnit(spawnPosition, Quaternion.identity, transform);
                    UnitView newUnitView = unitInstance.GetComponent<UnitView>();

                    // 4. INITIALIZE FRESH UNIT VIEW
                    var emittedNode = gameCore.ActiveLevelData.Grid.Matrix.Find(c => c.Position.X == spawnCoord.x && c.Position.Y == spawnCoord.y);
                    newUnitView.Initialize(emittedNode);

                    // 5. REGISTER REGISTRY SLOT
                    activeBoardReferences.UnitViews[spawnCoord] = newUnitView;

                    // 6. UPDATE VISUAL PIPE COUNTER AT SOURCE
                    if (activeBoardReferences.UnitViews.TryGetValue(pipeCoord, out var pipeView))
                    {
                        var pipeNode = gameCore.ActiveLevelData.Grid.Matrix.Find(c => c.Position.X == pipeCoord.x && c.Position.Y == pipeCoord.y);
                        if (pipeNode != null && pipeNode.ContinuousPipe != null)
                        {
                            int countLeft = pipeNode.ContinuousPipe.ReservoirQueue.Count;
                            pipeView.UpdatePipeCounter(countLeft);
                        }
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

    /// <summary>
    /// Core hook triggered when an element on the grid thaws out or clears a path dependency.
    /// </summary>
    private void HandleUnitUnblocked(int unitId)
    {
        if (activeBoardReferences == null) return;

        UnitView targetView = null;
        foreach (var view in activeBoardReferences.UnitViews.Values)
        {
            if (view != null && view.UnitId == unitId)
            {
                targetView = view;
                break;
            }
        }

        if (targetView != null)
        {
            // 1. Hide lock overlays or icons
            // 2. If it was hidden ("?"), clear the text overlay and set up nested interior assets
        }
    }

    /// <summary>
    /// Core hook triggered when a unit's ice layer drops or breaks completely.
    /// </summary>
    private void HandleUnitIceChanged(int unitId, int remainingIceLayers)
    {
        if (activeBoardReferences == null) return;

        UnitView targetView = null;
        foreach (var view in activeBoardReferences.UnitViews.Values)
        {
            if (view != null && view.UnitId == unitId)
            {
                targetView = view;
                break;
            }
        }

        if (targetView != null)
        {
            // Accessing the local lowercase field from your script block
            Color naturalColor = DamplingGameUtils.GetColorByIndex(targetView.unitColorIndex);

            if (remainingIceLayers > 0)
            {
                targetView.UpdateIceLayers(remainingIceLayers, naturalColor);
            }
            else
            {
                targetView.ShatterIce(naturalColor);
            }
        }
    }

    
    /// <summary>
    /// Core hook triggered when a targeted key is successfully collected by the player.
    /// </summary>
    private void HandleLockKeyCollected(int lockedUnitId, int collectedKeyUnitId)
    {
        if (activeBoardReferences == null) return;

        // Scan values sequentially since the collection is keyed by coordinate vectors instead of integer IDs
        UnitView lockedView = null;
        foreach (var view in activeBoardReferences.UnitViews.Values)
        {
            if (view != null && view.UnitId == lockedUnitId)
            {
                lockedView = view;
                break;
            }
        }

        if (lockedView != null)
        {
            lockedView.LockUnlocked();
        }
    }

    /// <summary>
    /// Core hook triggered when a unit within an activated link chain executes its gameplay action.
    /// </summary>
    private void HandleLinkedUnitPlayed(int unitId)
    {
        if (activeBoardReferences == null) return;

        // Locate the corresponding view for the unit that was part of the triggered chain
        foreach (var view in activeBoardReferences.UnitViews.Values)
        {
            if (view != null && view.UnitId == unitId)
            {
                // 1. Tell this specific unit view to release its physical contents onto the board
                // (Replace this method call with your exact visualizer method name that handles launching/spawning the balls)
                view.LinkedUnitPlayed();

                // 2. Clear or update any structural text layers or lid states on the view, 
                // but DO NOT disable the gameObject or return assets to the pool yet!
                break;
            }
        }
    }

    public void ClearActiveBoard()
    {
        if (activeBoardReferences == null) return;


        // 2. Release all your standard unit views as well
        foreach (var unitView in activeBoardReferences.UnitViews.Values)
        {
            if (unitView != null)
            {
                unitView.gameObject.SetActive(false); // Or PoolManager.Despawn(unitView.gameObject);
            }
        }
        activeBoardReferences.UnitViews.Clear();

        foreach (var ball in ballViews)
            DamplingObjectPool.Instance.ReturnBall(ball.gameObject);

        ballViews.Clear();

    }

}