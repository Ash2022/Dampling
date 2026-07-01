using UnityEngine;
using System.Collections.Generic;
using static GameLevelSchema;
using System.Threading.Tasks;

public class GameManager : MonoBehaviour
{
    public enum GameState
    {
        Initializing,
        ReadyToPlay,
        ProcessingInput,
        GameEnded
    }

    public static GameManager Instance { get; private set; }

    [SerializeField] private LevelVisualization levelVisualization;

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
                    StartLevel(CurrentLevelIndex);
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
/*
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 mousePos2D = new Vector2(mouseWorldPos.x, mouseWorldPos.y);

            RaycastHit2D hit = Physics2D.Raycast(mousePos2D, Vector2.zero);

            if (hit.collider != null)
            {
                Debug.Log($"SUCCESS: Manually hit collider named: {hit.collider.name}");
            }
            else
            {
                Debug.Log("FAIL: Mouse clicked empty space. No 2D colliders detected under the pointer.");
            }
        }
    }*/
}