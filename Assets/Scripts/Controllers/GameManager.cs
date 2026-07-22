using UnityEngine;
using System.Collections.Generic;
using static GameLevelSchema;
using System.Threading.Tasks;
using System;
using System.Linq;
using UnityEngine.InputSystem;
using DG.Tweening;
using TMPro;

public class GameManager : MonoBehaviour
{
    public const int BELT_CAPACITY = 30;
    public enum GameState
    {
        Initializing, Pause, ShowingTut, ReadyToPlay, ProcessingInput, BeltJammed, GameEnded, ShowingEndScreen, Magnet
    }
    public static GameManager Instance { get; private set; }

    [SerializeField] private LevelVisualization levelVisualization;
    [SerializeField] private BeltGenerator beltGenerator;
    [SerializeField] private BoosterManager boosterManager;
    private DamplingGameCore gameCore;
    private GameLevelSchema currentLevelData;
    public GameState currentState;

    [SerializeField] private UIManager uiManager;          // assign in scene
    [SerializeField] private GameOverView gameOverView;    // assign in scene
    [SerializeField] private GameObject splashScreen;
    [SerializeField] private TMP_Text splashText;
    [SerializeField] private ReviveView reviveView;
    [SerializeField] PausePanelView _pausePanel;

    private GameStateEvaluator gameStateEvaluator;


    // Persistent progression tracker
    public int CurrentLevelIndex = 0;

    private float checkTimer = 0f;

    public int BallsInStagingArea { get; set; }
    public GameState CurrGameState => currentState;

    private BoardVisualReferences activeBoardReferences;
    public List<BallView> ballViews = new List<BallView>();

    internal void AddToBalanceVisual(int amount) => uiManager.AddToBalanceVisual(amount);
    internal Vector3 GetBalanceRect() => uiManager.GetBalancePosition();
    public void MoveBalanceUp() => uiManager.MoveBalanceUpOnSort();
    internal void SetUIToBalance() => uiManager.SetBalanceToModel();
    public bool IsGameOver() => currentState == GameState.GameEnded;

    private void Awake()
    {
        Instance = this;
        currentState = GameState.Initializing;
        DontDestroyOnLoad(gameObject);
    }

    private async Task Start()
    {
        Application.targetFrameRate = 60;

        // Safe camera sizing
        float baselineAspect = 9f / 16f;
        float baselineOrthoSize = 5f;
        float targetAspect = Camera.main.aspect;
        if (targetAspect < baselineAspect)
        {
            float adjustedOrthoSize = baselineOrthoSize * (baselineAspect / targetAspect);
            Camera.main.orthographicSize = adjustedOrthoSize;
        }

        splashScreen.SetActive(true);

        await InitializeGame();

        if (CurrentLevelIndex == -1)
        {
            CurrentLevelIndex = ModelManager.Instance.GetLastPlayedLevel();
            CurrentLevelIndex++;
        }
        if (CurrentLevelIndex == 0)
        {
            splashScreen.SetActive(false);
            StartLevel(CurrentLevelIndex);
        }
        else
        {
            splashText.text = "CLICK TO CONTINUE";
        }
    }

    public async Task InitializeGame()
    {
        currentState = GameState.Initializing;
        beltGenerator.InitializeBelt(BELT_CAPACITY);

        boosterManager.Initialize(this, beltGenerator, uiManager);
        // Step 1: Await the multi-frame async memory instantiation allocation loop
        await DamplingObjectPool.Instance.InitializePoolsAsync();
        // Step 2: Proceed with standard model loading and data processing now that objects are ready
        ModelManager.Instance.Initialize();
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

        activeBoardReferences.logicalContainerPositions.Clear();
        activeBoardReferences.ContainerViews.Clear();

        foreach (var ball in ballViews)
            DamplingObjectPool.Instance.ReturnBall(ball.gameObject);

        ballViews.Clear();

        beltGenerator.ResetSlots();

        checkTimer = 0f;
        BallsInStagingArea = 0;
    }

    public void SplashClicked()
    {
        splashScreen.SetActive(false);
        StartLevel(CurrentLevelIndex);
    }

    private void Update()
    {
        if (currentState == GameState.GameEnded || currentState == GameState.ShowingEndScreen ||
            currentState == GameState.BeltJammed || currentState == GameState.Initializing
            || currentState == GameState.ShowingTut || currentState == GameState.Pause)

            return;

        // Cheats for level switching using the new Input System.
        if (Keyboard.current == null) return; // Input system not ready.

        if (Keyboard.current.upArrowKey.wasPressedThisFrame)
        {
            int nextLevelIndex = CurrentLevelIndex + 1;
            if (nextLevelIndex < ModelManager.Instance.LevelCount)
                StartLevel(nextLevelIndex);
        }

        if (Keyboard.current.downArrowKey.wasPressedThisFrame)
        {
            int prevLevelIndex = CurrentLevelIndex - 1;
            if (prevLevelIndex >= 0)
                StartLevel(prevLevelIndex);
        }

        if (Keyboard.current.mKey.wasPressedThisFrame)
        {
            ModelManager.Instance.AdjustMagnetCount(3);
            boosterManager.RefreshButtonVisuals(BoosterButtonView.BoosterType.Magnet);
            return;
        }

        if (Keyboard.current.sKey.wasPressedThisFrame)
        {
            ModelManager.Instance.AdjustShuffleCount(3);
            boosterManager.RefreshButtonVisuals(BoosterButtonView.BoosterType.Shuffle);
            return;
        }

        // Only check if we are in the playing state
        checkTimer += Time.deltaTime;
        if (checkTimer >= 1f) // Check twice a second
        {
            if (gameStateEvaluator.CheckForVisualDeadlock())
            {
                currentState = GameState.BeltJammed;
                beltGenerator.StopBeltMovement();

                //offer the revive
                reviveView.ShowRevive((answerBack) =>
                {
                    if (answerBack)
                    {
                        SoundsManager.Instance.PlayRevive();
                        //revive requested
                        boosterManager.ExecuteRevive();
                        ModelManager.Instance.AddToBalanceAndSave(-ModelManager.Instance.GetReviveCost());
                        ModelManager.Instance.UseRevive();
                        uiManager.SetBalanceToModelAnimate();
                        currentState = GameState.ReadyToPlay;
                    }
                    else
                    {
                        //no revive
                        ResumeGameOver(false);
                    }
                }, ModelManager.Instance.GetReviveCost());
            }
            checkTimer = 0f;
        }
    }

    public void MagnetClicked()
    {
        if (currentState == GameState.Magnet)
            currentState = GameState.ReadyToPlay;
        else if (currentState == GameState.ReadyToPlay)
            currentState = GameState.Magnet;
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

        bool isHardLevel = currentLevelData.HardLevel;
        int unlockedIndex = ModelManager.Instance.GetUnlock(CurrentLevelIndex);
        bool showTutorial = isHardLevel || unlockedIndex > 0;

        
        // Step 3: Wipe past scene instances and render the fresh board layout array mapping setup
        activeBoardReferences = levelVisualization.RenderInitialBoard(currentLevelData);

        // Step 2: Spin up a fresh simulation core instance to clear past game states completely
        gameCore = new DamplingGameCore();
        gameCore.InitializeLevel(currentLevelData, isLiveMode: true, HandleUnitIceChanged);

        gameStateEvaluator = new GameStateEvaluator(this, beltGenerator, activeBoardReferences);
        boosterManager.InitLevel(gameCore, activeBoardReferences, CurrentLevelIndex);

        uiManager.InitLevel(CurrentLevelIndex, ModelManager.Instance.GetBalance()
        , ModelManager.Instance.GetUnlock(CurrentLevelIndex), currentLevelData.HardLevel, showTutorial);


        beltGenerator.StartBeltMovement();

        if (showTutorial)
            currentState = GameState.ShowingTut;
        else
            currentState = GameState.ReadyToPlay;
    }

    internal void TutorialClicked()
    {
        uiManager.HideTutorial();
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

        SoundsManager.Instance.UnitPlayed();

        currentState = GameState.ProcessingInput;

        if(CurrentLevelIndex==0)
            uiManager.HideTutorialHand();

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

                case DamplingGameCore.EngineEventType.PipeEmittedUnit:
                    // 1. Unpack composite coordinates
                    var coords = (Tuple<Vector2Int, Vector2Int>)ev.Payload;
                    Vector2Int spawnCoord = coords.Item1;
                    Vector2Int pipeCoord = coords.Item2;

                    // 2. Locate origin (pipe) and destination world positions
                    var pipeView = activeBoardReferences.UnitViews[pipeCoord];
                    Vector3 startPosition = pipeView.transform.position;

                    Vector3 targetPosition = Vector2.zero;

                    if (activeBoardReferences.UnitViews.TryGetValue(spawnCoord, out var oldView))
                    {
                        if (oldView != null)
                        {
                            targetPosition = oldView.transform.position;
                            // Return oldView components/balls to pool here if needed
                        }
                    }

                    // Get world position of the destination cell layout space
                    //Vector3 targetPosition = activeBoardReferences.GetCellWorldPosition(spawnCoord);

                    // 3. Spawn unit from pool at the pipe's mouth
                    GameObject unitInstance = DamplingObjectPool.Instance.GetUnit(startPosition, Quaternion.identity, transform);
                    UnitView newUnitView = unitInstance.GetComponent<UnitView>();

                    // Prepare unit visuals/contents but keep interactivity disabled during transit
                    var emittedNode = gameCore.ActiveLevelData.Grid.Matrix.Find(c => c.Position.X == spawnCoord.x && c.Position.Y == spawnCoord.y);
                    //newUnitView.Initialize(emittedNode);

                    newUnitView.PipeInitialize(emittedNode);

                    // Scale down slightly to look like it is emerging inside the pipe
                    newUnitView.transform.localScale = Vector3.zero;

                    SoundsManager.Instance.PipeEmit();
                    // 5. Run sequential slide out animation after clicked unit finishes clearing the cell
                    AnimatePipeEmission(newUnitView, targetPosition, spawnCoord, emittedNode, (() =>
                    {
                        // 4. Update the visual counter at the pipe source immediately
                        var pipeNode = gameCore.ActiveLevelData.Grid.Matrix.Find(c => c.Position.X == pipeCoord.x && c.Position.Y == pipeCoord.y);
                        pipeView.UpdatePipeCounter(pipeNode.ContinuousPipe.ReservoirQueue.Count);
                    }));

                    break;
            }
        }
        currentState = GameState.ReadyToPlay;
    }

    private void ResumeGameOver(bool isWin)
    {
        if (currentState == GameState.GameEnded)
            return;

        uiManager.GameOver();
        currentState = GameState.GameEnded;

        if (isWin)
            ModelManager.Instance.AddToBalanceAndSave(ModelManager.GOLD_PER_WIN);

        gameOverView.InitEndScreen(isWin, CurrentLevelIndex, () =>
        {
            if (isWin)
            {
                ModelManager.Instance.SetLastPlayedLevel(CurrentLevelIndex);
                CurrentLevelIndex++;
            }

            /*
                        int unlockIndex = ModelManager.Instance.GetUnlock(CurrentLevelIndex);

                        if (unlockIndex != -1)
                            uiManager.ShowTutorialImage(true, unlockIndex + 1);
                        else
                        {*/
            StartLevel(CurrentLevelIndex);
            //}
        });
    }


    public void AdvanceContainerQueue(int queueIndex, ContainerView resolvedView)
    {
        // Check for game over win
        if (gameStateEvaluator.CheckForVisualWin())
        {
            Debug.Log("VISUAL WIN! All containers resolved. Loading next level...");
            ResumeGameOver(true);
            return;
        }

        float containerSpacingY = 0.57f;

        // 1. Ensure the resolved container's position is tracked before we use it
        if (!activeBoardReferences.logicalContainerPositions.ContainsKey(resolvedView))
        {
            activeBoardReferences.logicalContainerPositions[resolvedView] = resolvedView.transform.position;
        }

        // Get the mathematical Y position this container was sitting at
        float resolvedLogicalY = activeBoardReferences.logicalContainerPositions[resolvedView].y;

        // 2. Find ALL active containers in this queue, regardless of where they physically are right now
        var containersInQueue = activeBoardReferences.ContainerViews.Values
            .Where(v => v != null && v.QueueIndex == queueIndex && v != resolvedView && v.gameObject.activeInHierarchy)
            .ToList();

        foreach (var container in containersInQueue)
        {
            // 3. Register any container that hasn't moved yet
            if (!activeBoardReferences.logicalContainerPositions.ContainsKey(container))
            {
                activeBoardReferences.logicalContainerPositions[container] = container.transform.position;
            }

            // 4. Compare their LOGICAL positions to see if they are sitting behind the resolved container
            if (activeBoardReferences.logicalContainerPositions[container].y > resolvedLogicalY + 0.1f)
            {
                // Calculate the absolute new position they need to end up at
                Vector3 newTargetPos = activeBoardReferences.logicalContainerPositions[container] - new Vector3(0f, containerSpacingY, 0f);

                // Save this new intended target so subsequent Magnet loops in the same frame know about it
                activeBoardReferences.logicalContainerPositions[container] = newTargetPos;

                // Safely kill the old tween and smoothly glide to the new target
                container.transform.DOKill();
                container.transform.DOMove(newTargetPos, 0.3f).SetEase(Ease.OutBack);

                if (Mathf.Abs(newTargetPos.y - levelVisualization.QueueBottomY) < 0.05f || newTargetPos.y <= levelVisualization.QueueBottomY)
                {
                    container.RevealContainerColor();
                }

            }
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
            Color naturalColor = Color.white;// DamplingGameUtils.GetColorByIndex(targetView.unitColorIndex);

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



    internal void BallEnteredSlot(BallView ballView)
    {
        BallsInStagingArea--;

        if (gameStateEvaluator.CheckForLogicalWin())
        {
            //Debug.Log("LOGICAL WIN! All units played. Waiting for animations to finish...");
            // TODO: Fire off early confetti, change background music, or disable a pause menu here.
            beltGenerator.IncreaseBeltSpeed();
        }
    }

    public void BallEnteredOrExitSlot()
    {
        beltGenerator.CheckBeltFullness();
    }

    public void UseMagnetBooster(UnitView targetedUnitView)
    {
        boosterManager.ExecuteMagnet(targetedUnitView);
        currentState = GameState.ReadyToPlay;
    }

    public void EvaluateLogicalWinState()
    {
        if (gameStateEvaluator.CheckForVisualWin())
        {
            Debug.Log("VISUAL WIN! All containers resolved. Loading next level...");
            ResumeGameOver(true);
        }
    }

    internal bool IsMagnet()
    {
        return currentState == GameState.Magnet;
    }

    private void AnimatePipeEmission(UnitView unitView, Vector3 targetPosition, Vector2Int spawnCoord, CellNode emittedNode, Action done)
    {
        DG.Tweening.Sequence emitSequence = DG.Tweening.DOTween.Sequence();

        // Introduce a delay matching the active unit's fly/fade duration so it doesn't overlap
        emitSequence.AppendInterval(0.75f);

        // Animate scale up and slide down simultaneously to simulate emerging from the mouth
        emitSequence.Append(unitView.transform.DOScale(Vector3.one * 0.375f, 0.2f).SetEase(DG.Tweening.Ease.OutQuad));
        emitSequence.Join(unitView.transform.DOMove(targetPosition, 0.5f).SetEase(DG.Tweening.Ease.OutBack));

        emitSequence.OnComplete(() =>
        {
            // Finalize registration and restore gameplay interactions once fully seated
            activeBoardReferences.UnitViews[spawnCoord] = unitView;
            unitView.Initialize(emittedNode);
            unitView.RemoveLidCover();
            EvaluateLogicalWinState();
            done?.Invoke();
        });
    }

    internal UnitView GetLockUnitView(int keyLockPairIndex)
    {
        return activeBoardReferences.UnitViews.Values.First(view =>
            view.ModelData != null &&
            view.ModelData.OccupyingUnit != null &&
            view.ModelData.OccupyingUnit.KeyLockPairIndex == keyLockPairIndex &&
            view.ModelData.OccupyingUnit.ExplicitlyBlockedByUnitIds.Count > 0);
    }

    internal UnitView GetUnitView(int unitId)
    {
        return activeBoardReferences.UnitViews.Values.First(view => view.UnitId == unitId);
    }

    public void PauseMenuClicked()
    {
        beltGenerator.StopBeltMovement();
        currentState = GameState.Pause;
        _pausePanel.ShowPanel(async (pauseAnswer) =>
        {
            _pausePanel.HidePanel();

            if (pauseAnswer == PausePanelView.PauseAnswer.Resume)
            {
                currentState = GameState.ReadyToPlay;
                beltGenerator.ResumeBelt();

            }

            else if (pauseAnswer == PausePanelView.PauseAnswer.Restart)
            {
                //AnalyticsManagerGame.Instance.LogLevelComplete(_currLevel.Index, 0, false, _numFailsInLevel, GetCurrentProgress(), isCurrentKillMode);
                StartLevel(CurrentLevelIndex);
            }
            else if (pauseAnswer == PausePanelView.PauseAnswer.DeleteAll)
            {
                ModelManager.Instance.DeleteData();
                StartLevel(0);
            }
        });
    }

    public UnitView GetUnitViewAtPosition(int x, int y)
    {
        foreach (var kvp in activeBoardReferences.UnitViews)
        {
            var unitView = kvp.Value;
            if (unitView != null && unitView.ModelData.Position.X == x && unitView.ModelData.Position.Y == y)
            {
                return unitView;
            }
        }
        return null;
    }

}