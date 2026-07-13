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
        Initializing,
        ReadyToPlay,
        ProcessingInput,
        BeltJammed,

        GameEnded,
        ShowingEndScreen
    }

    public static GameManager Instance { get; private set; }

    [SerializeField] private LevelVisualization levelVisualization;
    [SerializeField] private BeltGenerator beltGenerator;

    private DamplingGameCore gameCore;
    private GameLevelSchema currentLevelData;
    private GameState currentState;

    [SerializeField] private UIManager uiManager;          // assign in scene
    [SerializeField] private GameOverView gameOverView;    // assign in scene
    [SerializeField] private GameObject splashScreen;
    [SerializeField] private TMP_Text splashText;
    [SerializeField] private ReviveView reviveView;

    // Persistent progression tracker
    public int CurrentLevelIndex = 0;

    private float checkTimer = 0f;
    public int BallsInStagingArea { get; private set; }

    private BoardVisualReferences activeBoardReferences;
    public List<BallView> ballViews = new List<BallView>();

    internal void AddToBalanceVisual(int amount) => uiManager.AddToBalanceVisual(amount);
    internal Vector3 GetBalanceRect() => uiManager.GetBalancePosition();
    public void MoveBalanceUp() => uiManager.MoveBalanceUpOnSort();
    internal void SetUIToBalance() => uiManager.SetBalanceToModel();

    private void Awake()
    {
        Instance = this;
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

        //StartLevel(CurrentLevelIndex);
    }

    public void SplashClicked()
    {
        splashScreen.SetActive(false);
        StartLevel(CurrentLevelIndex);
    }

    private void Update()
    {

        if(currentState == GameState.GameEnded || currentState == GameState.ShowingEndScreen ||
            currentState == GameState.BeltJammed)
            return;
            

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

        // Only check if we are in the playing state
        checkTimer += Time.deltaTime;
        if (checkTimer >= 0.5f) // Check twice a second
        {
            if (CheckForVisualDeadlock())
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
                        ExecuteRevive();                        
                        ModelManager.Instance.AddToBalanceAndSave(-ModelManager.REVIVE_COST);
                        uiManager.SetBalanceToModelAnimate();
                    }
                    else
                    {
                        //no revive
                        ResumeGameOver(false);
                    }
                });

            }

            checkTimer = 0f;
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

        uiManager.InitLevel(CurrentLevelIndex, ModelManager.Instance.GetBalance());

        // Step 3: Wipe past scene instances and render the fresh board layout array mapping setup
        activeBoardReferences = levelVisualization.RenderInitialBoard(currentLevelData);

        // Step 2: Spin up a fresh simulation core instance to clear past game states completely
        gameCore = new DamplingGameCore();
        gameCore.InitializeLevel(
            currentLevelData,
            isLiveMode: true,
            HandleUnitUnblocked,
            HandleUnitIceChanged,
            HandleLockKeyCollected,
            HandleLinkedUnitPlayed
        );

        beltGenerator.StartBeltMovement();

        currentState = GameState.ReadyToPlay;
    }

    public bool IsGameOver()
    {
        return currentState == GameState.GameEnded;
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

        /*
        if (gameCore.IsGameOver)
        {
            //currentState = GameState.GameEnded;
            //Debug.Log(("Game Over! Belt Full"));
            //return;
        }
        */

        currentState = GameState.ReadyToPlay;
    }

    private void ResumeGameOver(bool isWin)
    {
        if(currentState == GameState.GameEnded)
            return;

        uiManager.GameOver();
        currentState = GameState.GameEnded;
        StopAllCoroutines();


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
        //a container was resolved - we can check for game over win
        if (CheckForVisualWin())
        {
            Debug.Log("VISUAL WIN! All containers resolved. Loading next level...");
            ResumeGameOver(true);
            // Advance progression tracking systematically
            //CurrentLevelIndex++;
            //StartLevel(CurrentLevelIndex);
            return;
        }


        // 1. Fixed spacing delta based directly on your layout rules
        float containerSpacingY = 0.57f;

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

        beltGenerator.ResetSlots();

        checkTimer = 0f;
        BallsInStagingArea = 0;
    }

    private bool CheckForVisualDeadlock()
    {
        // 1. Is the visual belt completely jammed?
        bool slotsFull = beltGenerator.AllSlotsFull();
        if (!slotsFull) return false;

        // 2. What colors are physically stuck on the belt right now?
        List<int> beltColors = beltGenerator.GetBeltsColors();

        // 3. What colors are physically waiting at the front of the queues right now?
        List<int> activeContainerColors = GetVisualAvailableContainerColors();

        // 4. Can ANY ball on the belt go into ANY open container?
        bool matchPossible = beltColors.Any(color => activeContainerColors.Contains(color));

        if (!matchPossible)
        {
            return true;
            //currentState = GameState.GameEnded;
            Debug.Log("Visual Deadlock! The belt is full and no items match the active containers.");
            // TODO: Trigger Visual Game Over UI / Defeat sequence here
        }

        return false;
    }

    /// <summary>
    /// Scans the actual UI elements to find which containers are currently at the front of the line.
    /// </summary>
    private List<int> GetVisualAvailableContainerColors()
    {
        List<int> resolvableColors = new List<int>();

        if (activeBoardReferences == null || activeBoardReferences.ContainerViews == null)
            return resolvableColors;

        // 1. Grab all active, unresolved containers from the board references
        // (Assuming your ContainerView has a way to check if it's already full, like 'Capacity > 0' or '!IsResolved')
        var unresolvedContainers = activeBoardReferences.ContainerViews.Values
            .Where(v => v != null && v.gameObject.activeInHierarchy && v.HasRoomLeft());

        // 2. Group them visually by their Queue column
        var visualQueues = unresolvedContainers.GroupBy(v => v.QueueIndex);

        foreach (var queueGroup in visualQueues)
        {
            // 3. FIND THE VISUAL HEAD:
            // Since your AdvanceContainerQueue moves containers DOWN (-Y), 
            // the one with the lowest Y position is physically at the front of the line.
            var headContainer = queueGroup.OrderBy(v => v.transform.position.y).FirstOrDefault();

            if (headContainer != null)
            {
                resolvableColors.Add(headContainer.Model.ColorIndex);
            }
        }

        return resolvableColors.Distinct().ToList();
    }

    /// <summary>
    /// Detects if the puzzle is mathematically solved based on active visual elements and staging limits.
    /// </summary>
    private bool CheckForLogicalWin()
    {
        if (activeBoardReferences == null || activeBoardReferences.UnitViews == null) return false;

        // 1. Are there any active, unplayed units left on the board?
        foreach (var unit in activeBoardReferences.UnitViews.Values)
        {
            // Assuming your UnitView disables its gameObject when it finishes firing,
            // or has a property like 'HasReleasedContents'. Update this check to match your logic!
            if (unit != null && unit.gameObject.activeInHierarchy)
            {
                return false; // Units are still on the board, game is not won yet.
            }
        }

        // 2. All units are played! Now, calculate available space.
        // Assuming GetBeltsColors() returns the list of balls currently occupying a slot on the belt.
        int currentBallsOnBelt = beltGenerator.GetBeltsColors().Count;
        int emptyBeltSlots = BELT_CAPACITY - currentBallsOnBelt;

        // 3. The Final Equation
        // If the flying balls fit in the remaining belt slots, it's a guaranteed win.
        if (BallsInStagingArea <= emptyBeltSlots)
        {
            return true;
        }

        return false; // The staging area has more balls than the belt can hold -> Impending Jam / Loss
    }

    /// <summary>
    /// Detects if every container on the screen has been fully resolved (Capacity reached 0).
    /// This means all animations are done and the level is visually complete.
    /// </summary>
    private bool CheckForVisualWin()
    {
        if (activeBoardReferences == null || activeBoardReferences.ContainerViews == null) return false;

        // Scan all physical container views. If ANY container is still active and hungry, no win yet.
        foreach (var container in activeBoardReferences.ContainerViews.Values)
        {
            if (container != null && container.gameObject.activeInHierarchy && container.HasRoomLeft())
            {
                return false;
            }
        }

        return true;
    }

    public void RegisterBallEnteredStaging()
    {
        BallsInStagingArea++;
        //Debug.Log($"[Staging Tracker] Ball Airborne! Total in Staging: {BallsInStagingArea}");
    }

    public void RegisterBallLandedOnBelt()
    {
        BallsInStagingArea--;
        //Debug.Log($"[Staging Tracker] Ball Landed! Total in Staging: {BallsInStagingArea}");
    }

    public void BallLinked(BallView ballView)
    {
        ballViews.Add(ballView);
        
    }

    public void BallEmittedToStage(BallView ballView)
    {
        RegisterBallEnteredStaging();
    }

    internal void BallEnteredSlot(BallView ballView)
    {
        RegisterBallLandedOnBelt();

        if (CheckForLogicalWin())
        {
            Debug.Log("LOGICAL WIN! All units played. Waiting for animations to finish...");
            // TODO: Fire off early confetti, change background music, or disable a pause menu here.
            uiManager.ShowHideSkipButton(true);
        }
    }

    public void ExecuteRevive()
    {
        if (currentState != GameState.GameEnded && currentState != GameState.BeltJammed) return;

        // 1. Get the current colors on the belt
        List<int> beltColors = beltGenerator.GetBeltsColors();

        // 2. Group by color, sort by frequency (highest first), and take the top 2
        var topColors = beltColors.GroupBy(c => c)
                                  .OrderByDescending(g => g.Count())
                                  .Select(g => new { ColorIndex = g.Key, Count = g.Count() })
                                  .Take(2)
                                  .ToList();

        if (topColors.Count == 0) return; // Safety check

        // The coordinates you requested for the new units (Row -1, Col 0 and 1)
        Vector2Int[] spawnCoords = { new Vector2Int(3, -1), new Vector2Int(4, -1) };

        for (int i = 0; i < topColors.Count; i++)
        {
            int color = topColors[i].ColorIndex;
            // Cap the extraction at 9 balls max
            int ballsToExtract = Mathf.Min(9, topColors[i].Count);

            // 3. Physically remove the balls from the belt (See Step 2 below for this method)
            beltGenerator.ExtractBallsByColor(color, ballsToExtract);

            // 4. Create the raw Data Node for the Core (See Step 3 below for this method)
            var newNode = gameCore.InjectReviveUnit(spawnCoords[i].x, spawnCoords[i].y, color, ballsToExtract);

            // 5. Calculate physical Unity spawn position
            // We look at Row 0 to figure out where Row -1 should visually sit.
            Vector3 spawnPosition = i == 0 ? new Vector2(-0.3f,-0.424f):new Vector2(0.3f,-0.424f);
            
            // 6. Spawn the visual unit
            GameObject unitInstance = DamplingObjectPool.Instance.GetUnit(spawnPosition, Quaternion.identity, transform);
            UnitView newUnitView = unitInstance.GetComponent<UnitView>();

            newUnitView.Initialize(newNode);
            activeBoardReferences.UnitViews[spawnCoords[i]] = newUnitView;
        }

        // 7. Fix the staging math (since we ripped balls out of the ecosystem)
        // Recalculate or explicitly reset staging math if necessary here.

        // 8. Resume the game
        currentState = GameState.ReadyToPlay;
        // Assuming you have a method to resume belt visuals
        // beltGenerator.ResumeBelt(); 

        Debug.Log("Revive Executed! New units spawned at Row -1.");

        beltGenerator.ResumeBelt();
    }

    public void SkipClicked()
    {
        ResumeGameOver(true);
    }
    
}