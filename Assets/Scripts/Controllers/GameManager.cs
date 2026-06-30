using UnityEngine;
using System.Collections.Generic;
using static GameLevelSchema;

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

    private void Start()
    {
        InitializeGame();
    }

    /// <summary>
    /// Natively executes EXACTLY ONCE at boot-up to set up persistent static subsystems.
    /// </summary>
    public void InitializeGame()
    {
        currentState = GameState.Initializing;

        // Initialize persistent asset databases once
        ModelManager.Instance.Initialize();

        // Boot-up pipeline complete, route automatically into the first level loop sequence
        StartLevel(CurrentLevelIndex);
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
}