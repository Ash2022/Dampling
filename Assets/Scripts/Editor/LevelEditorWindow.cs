using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;


public class LevelEditorWindow : EditorWindow
{
    public const int BELT_MAX = 28;

    // Window settings and state configuration
    private int gridColumns = 6;
    private int gridRows = 8;
    private int queueCount = 4;
    private int colorCount = 4;

    // Local editor working representation of the grid map layout
    private enum CellBehavior { Standard, Blocker, Pipe }
    private class EditorCell
    {
        public CellBehavior Behavior = CellBehavior.Standard;
        public int PipeEmissions = 3;
        public string AssignedColorId = ""; // Track color assigned during populate
        public bool StartHidden = false;
    }

    private Dictionary<Vector2Int, EditorCell> editorMatrix = new Dictionary<Vector2Int, EditorCell>();
    private List<List<GameLevelSchema.ContainerData>> generatedQueues = new List<List<GameLevelSchema.ContainerData>>();
    private bool isGridBuilt = false;

    // UI Drawing constants
    private const float CellSize = 55f;
    private const float Padding = 5f;
    private const float QueueWidth = 100f;
    private const float QueueContainerHeight = 24f;

    private string botReportOutputText = "";

    [MenuItem("Tools/Dampling Level Editor")]
    public static void ShowWindow()
    {
        GetWindow<LevelEditorWindow>("Dampling Editor");
    }

    private void OnGUI()
    {
        GUILayout.Label("Level Layout Configuration", EditorStyles.boldLabel);

        // --- Configuration Sliders ---
        gridColumns = EditorGUILayout.IntSlider("Grid Columns (X)", gridColumns, 1, 15);
        gridRows = EditorGUILayout.IntSlider("Grid Rows (Y)", gridRows, 1, 20);
        queueCount = EditorGUILayout.IntSlider("Container Queues", queueCount, 1, 8);
        colorCount = EditorGUILayout.IntSlider("Number of Colors", colorCount, 2, 10);

        EditorGUILayout.Space();

        // --- Control Buttons ---
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("1. Build Canvas Structure", GUILayout.Height(30)))
        {
            BuildCanvas();
        }
        EditorGUI.BeginDisabledGroup(!isGridBuilt);
        if (GUILayout.Button("2. Populate Balance (Zero-Sum)", GUILayout.Height(30)))
        {
            PopulateLevelData();
        }
        if (GUILayout.Button("Reset / Clear Data", GUILayout.Height(30)))
        {
            ClearPopulatedData();
        }
        EditorGUI.EndDisabledGroup();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save Level JSON")) { SaveLevelJson(); }
        if (GUILayout.Button("Load Level JSON")) { LoadLevelJson(); }
        GUILayout.EndHorizontal();

        EditorGUILayout.Space();

        EditorGUILayout.Space();
        GUILayout.Label("AI Diagnostic Agent Framework", EditorStyles.boldLabel);
        
        EditorGUI.BeginDisabledGroup(!isGridBuilt || generatedQueues.All(q => q.Count == 0));
        if (GUILayout.Button("🔬 Run Bot Stress Test (1000 Simulations)", GUILayout.Height(32)))
        {
            // 1. Gather our workspace variables into a valid serializable format object
            GameLevelSchema transientLevelData = AssembleActiveEditorStateToSchema();

            // 2. Fire the asynchronous simulation batch sequence
            DamplingSimulationAgent agent = new DamplingSimulationAgent();
            var analyticalResultReport = agent.RunBatchSimulation(transientLevelData, 1000);

            // 3. Format the lines clean into our workspace text log box
            botReportOutputText = string.Join("\n", analyticalResultReport.SummaryLog);
        }
        EditorGUI.EndDisabledGroup();

        // If a report exists, draw a scrollable text box to review the findings in editor
        if (!string.IsNullOrEmpty(botReportOutputText))
        {
            EditorGUILayout.LabelField("Simulation Summary Log Output:", EditorStyles.miniBoldLabel);
            EditorGUILayout.TextArea(botReportOutputText, GUILayout.Height(150));
            if (GUILayout.Button("Clear Report Console Log", GUILayout.Height(18)))
            {
                botReportOutputText = "";
            }
        }

        // --- Draw the Interactive Side-by-Side Workspace ---
        if (isGridBuilt)
        {
            DrawEditorWorkspaceSideBySide();
        }
    }

    private void BuildCanvas()
    {
        editorMatrix.Clear();
        generatedQueues.Clear();

        for (int y = 0; y < gridRows; y++)
        {
            for (int x = 0; x < gridColumns; x++)
            {
                editorMatrix[new Vector2Int(x, y)] = new EditorCell();
            }
        }

        for (int i = 0; i < queueCount; i++)
        {
            generatedQueues.Add(new List<GameLevelSchema.ContainerData>());
        }

        isGridBuilt = true;
    }

    private void ClearPopulatedData()
    {
        foreach (var cell in editorMatrix.Values)
        {
            cell.Behavior = CellBehavior.Standard; // NEW: Reset structural state to standard
            cell.PipeEmissions = 3;                // NEW: Restore the default pipe size parameter
            cell.AssignedColorId = "";             // Wipe the data-driven color layers
            cell.StartHidden = false;              // NEW: Clear the visibility modification state flag
        }

        foreach (var q in generatedQueues)
        {
            q.Clear();
        }

        botReportOutputText = ""; // Clear out old bot logs so the window refreshes clean
        Repaint();
    }

    private void PopulateLevelData()
    {
        int totalUnits = 0;
        foreach (var cell in editorMatrix.Values)
        {
            if (cell.Behavior == CellBehavior.Standard) totalUnits += 1;
            else if (cell.Behavior == CellBehavior.Pipe) totalUnits += cell.PipeEmissions;
        }

        if (totalUnits == 0)
        {
            EditorUtility.DisplayDialog("Error", "Cannot populate! There are zero playable units or pipe allocations on this board configuration.", "OK");
            return;
        }

        int totalContainersNeeded = totalUnits * 3;

        List<string> colorPalette = new List<string>();
        for (int i = 0; i < colorCount; i++)
        {
            colorPalette.Add($"Color_{i}");
        }

        List<string> unitColorAssignments = new List<string>();
        for (int i = 0; i < totalUnits; i++)
        {
            unitColorAssignments.Add(colorPalette[i % colorPalette.Count]);
        }

        System.Random rnd = new System.Random();
        unitColorAssignments = unitColorAssignments.OrderBy(x => rnd.Next()).ToList();

        // Assign colors back directly to the visible matrix fields
        int assignedCount = 0;
        foreach (var cell in editorMatrix.Values)
        {
            if (cell.Behavior == CellBehavior.Standard && assignedCount < unitColorAssignments.Count)
            {
                cell.AssignedColorId = unitColorAssignments[assignedCount++];
            }
            else if (cell.Behavior == CellBehavior.Pipe)
            {
                cell.AssignedColorId = unitColorAssignments[assignedCount % unitColorAssignments.Count];
                assignedCount += cell.PipeEmissions;
            }
            else
            {
                cell.AssignedColorId = "";
            }
        }

        List<GameLevelSchema.ContainerData> globalContainerPool = new List<GameLevelSchema.ContainerData>();
        foreach (var color in unitColorAssignments)
        {
            globalContainerPool.Add(new GameLevelSchema.ContainerData { ColorId = color });
            globalContainerPool.Add(new GameLevelSchema.ContainerData { ColorId = color });
            globalContainerPool.Add(new GameLevelSchema.ContainerData { ColorId = color });
        }
        globalContainerPool = globalContainerPool.OrderBy(x => rnd.Next()).ToList();

        foreach (var q in generatedQueues) q.Clear();
        for (int i = 0; i < globalContainerPool.Count; i++)
        {
            generatedQueues[i % queueCount].Add(globalContainerPool[i]);
        }

        //EditorUtility.DisplayDialog("Success", $"Populated Successfully!\nTotal Units: {totalUnits} ({totalUnits * 9} Damplings)\nTotal Demand Containers: {totalContainersNeeded}", "OK");
    }

    private void DrawEditorWorkspaceSideBySide()
    {
        Event e = Event.current;

        GUILayout.BeginHorizontal();

        // --- LEFT PANEL: MAIN SUPPLY GRID ---
        GUILayout.BeginVertical();
        // Calculate dynamic active units on the fly
        int activeUnitsCount = editorMatrix.Values.Sum(cell => 
            cell.Behavior == CellBehavior.Standard ? 1 : 
            cell.Behavior == CellBehavior.Pipe ? cell.PipeEmissions : 0
        );
        GUILayout.Label($"Supply Grid Layout (Active Units: {activeUnitsCount})", EditorStyles.boldLabel);

        float gridTotalWidth = gridColumns * (CellSize + Padding);
        float gridTotalHeight = gridRows * (CellSize + Padding);
        Rect gridArea = EditorGUILayout.GetControlRect(false, gridTotalHeight, GUILayout.Width(gridTotalWidth));

        for (int y = 0; y < gridRows; y++)
        {
            for (int x = 0; x < gridColumns; x++)
            {
                Vector2Int coord = new Vector2Int(x, y);
                if (!editorMatrix.ContainsKey(coord)) continue;

                EditorCell cell = editorMatrix[coord];
                float posX = gridArea.x + (x * (CellSize + Padding));
                float posY = gridArea.y + (y * (CellSize + Padding));
                Rect cellRect = new Rect(posX, posY, CellSize, CellSize);

                // Check if the cell directly below this one is configured as a Pipe
                Vector2Int belowCoord = new Vector2Int(x, y + 1);
                bool isTargetedByPipeBelow = editorMatrix.ContainsKey(belowCoord) &&
                                            editorMatrix[belowCoord].Behavior == CellBehavior.Pipe;

                // Define the background color asset based on behavior state
                Color displayColor = cell.Behavior switch
                {
                    CellBehavior.Blocker => new Color(0.25f, 0.25f, 0.25f),
                    CellBehavior.Pipe => new Color(0.18f, 0.44f, 0.72f), // Blue Pipe base
                    _ => isTargetedByPipeBelow ? new Color(0.25f, 0.55f, 0.85f, 0.4f) : // Semi-transparent blue outlet tint
                         !string.IsNullOrEmpty(cell.AssignedColorId) ? GetColorValue(cell.AssignedColorId) :
                         new Color(0.85f, 0.85f, 0.85f)
                };

                // Draw solid layout component block
                EditorGUI.DrawRect(cellRect, displayColor);

                bool hasColor = !string.IsNullOrEmpty(cell.AssignedColorId) && cell.Behavior != CellBehavior.Blocker;
                GUIStyle textStyle = hasColor ? EditorStyles.whiteMiniLabel : EditorStyles.centeredGreyMiniLabel;

                if (cell.Behavior == CellBehavior.Standard)
                {
                    string pipeTargetLabel = isTargetedByPipeBelow ? " [Outlet]" : "";
                    string contentLabel = hasColor ? $"({x},{y}){pipeTargetLabel}\n{cell.AssignedColorId}" : $"({x},{y}){pipeTargetLabel}\nUnit";
                    GUI.Label(cellRect, contentLabel, textStyle);

                    if (cell.StartHidden && hasColor)
                    {
                        Rect hiddenIndicatorRect = new Rect(cellRect.x + 2, cellRect.y + cellRect.height - 14, cellRect.width - 4, 12f);
                        EditorGUI.DrawRect(hiddenIndicatorRect, new Color(0f, 0f, 0f, 0.6f));
                        GUI.Label(hiddenIndicatorRect, "HIDDEN STATE", EditorStyles.whiteMiniLabel);
                    }
                }
                else if (cell.Behavior == CellBehavior.Blocker)
                {
                    GUI.Label(cellRect, "BLOCK", EditorStyles.whiteMiniLabel);
                }
                else if (cell.Behavior == CellBehavior.Pipe)
                {
                    Rect labelRect = new Rect(cellRect.x, cellRect.y + 2, cellRect.width, 16f);
                    string pipeLabelText = hasColor ? $"Pipe:{cell.AssignedColorId}" : "Pipe Anchor";
                    GUI.Label(labelRect, pipeLabelText, EditorStyles.whiteMiniLabel);

                    Rect inputRect = new Rect(cellRect.x + 6, cellRect.y + 32, cellRect.width - 12, 18f);
                    cell.PipeEmissions = EditorGUI.IntField(inputRect, cell.PipeEmissions);
                }

                if (cellRect.Contains(e.mousePosition) && e.type == EventType.ContextClick)
                {
                    GenericMenu menu = new GenericMenu();

                    // If it's a standard unit that has been populated with a color, show unit options
                    if (cell.Behavior == CellBehavior.Standard && !string.IsNullOrEmpty(cell.AssignedColorId))
                    {
                        menu.AddItem(new GUIContent("Unit/Start Revealed"), !cell.StartHidden, () => { cell.StartHidden = false; Repaint(); });
                        menu.AddItem(new GUIContent("Unit/Start Hidden"), cell.StartHidden, () => { cell.StartHidden = true; Repaint(); });
                        menu.AddSeparator("Unit/");
                        menu.AddItem(new GUIContent("Convert Cell/To Blocker (Hole)"), false, () => { cell.Behavior = CellBehavior.Blocker; cell.AssignedColorId = ""; cell.StartHidden = false; Repaint(); });
                        menu.AddItem(new GUIContent("Convert Cell/To Pipe Generator"), false, () => { cell.Behavior = CellBehavior.Pipe; cell.AssignedColorId = ""; cell.StartHidden = false; Repaint(); });
                    }
                    else // Otherwise, show the default structural cell options
                    {
                        menu.AddItem(new GUIContent("Set Standard Unit"), cell.Behavior == CellBehavior.Standard, () => { cell.Behavior = CellBehavior.Standard; cell.AssignedColorId = ""; cell.StartHidden = false; Repaint(); });
                        menu.AddItem(new GUIContent("Set Blocker (Hole)"), cell.Behavior == CellBehavior.Blocker, () => { cell.Behavior = CellBehavior.Blocker; cell.StartHidden = false; Repaint(); });
                        menu.AddItem(new GUIContent("Set Pipe Generator"), cell.Behavior == CellBehavior.Pipe, () => { cell.Behavior = CellBehavior.Pipe; cell.StartHidden = false; Repaint(); });
                    }

                    menu.ShowAsContext();
                    e.Use();
                }
            }
        }
        GUILayout.EndVertical();

        GUILayout.Space(30f);

        // --- RIGHT PANEL: RESOLUTION QUEUES ---
        GUILayout.BeginVertical();
        int totalContainersCount = generatedQueues.Sum(q => q.Count);
        GUILayout.Label($"Resolution Demand Queues (Total Containers: {totalContainersCount})", EditorStyles.boldLabel);

        float queuesTotalHeight = Mathf.Max(gridTotalHeight, 300f);
        Rect queueArea = EditorGUILayout.GetControlRect(false, queuesTotalHeight, GUILayout.Width(queueCount * (QueueWidth + Padding)));

        for (int q = 0; q < queueCount; q++)
        {
            float qX = queueArea.x + (q * (QueueWidth + Padding));
            Rect qLabelRect = new Rect(qX, queueArea.y, QueueWidth, 20f);
            EditorGUI.LabelField(qLabelRect, $"Queue Slot {q}", EditorStyles.boldLabel);

            int itemsInQueue = generatedQueues[q].Count;
            for (int c = 0; c < itemsInQueue; c++)
            {
                float containerY = queueArea.y + 25f + (c * (QueueContainerHeight + Padding));

                if (containerY + QueueContainerHeight > queueArea.y + queuesTotalHeight)
                {
                    Rect overflowRect = new Rect(qX, containerY, QueueWidth, 18f);
                    EditorGUI.LabelField(overflowRect, $"+{itemsInQueue - c} hidden...", EditorStyles.miniLabel);
                    break;
                }

                Rect containerRect = new Rect(qX, containerY, QueueWidth, QueueContainerHeight);
                string colorStr = generatedQueues[q][c].ColorId;

                EditorGUI.DrawRect(containerRect, GetColorValue(colorStr));

                GUIStyle labelOverride = new GUIStyle(EditorStyles.whiteBoldLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
                EditorGUI.LabelField(containerRect, colorStr.ToUpper(), labelOverride);
            }
        }
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
    }

    private Color GetColorValue(string colorId)
    {
        if (string.IsNullOrEmpty(colorId)) return Color.white;
        string[] parts = colorId.Split('_');
        if (parts.Length < 2 || !int.TryParse(parts[1], out int index)) return Color.gray;

        return index switch
        {
            0 => new Color(0.85f, 0.23f, 0.23f), // Vivid Red
            1 => new Color(0.18f, 0.67f, 0.18f), // Vivid Green
            2 => new Color(0.14f, 0.38f, 0.78f), // Deep Blue
            3 => new Color(0.88f, 0.72f, 0.12f), // Clear Yellow
            4 => new Color(0.53f, 0.18f, 0.68f), // Purple
            5 => new Color(0.12f, 0.72f, 0.72f), // Teal
            6 => new Color(0.88f, 0.45f, 0.12f), // Orange
            7 => new Color(0.44f, 0.26f, 0.12f), // Brown
            8 => new Color(0.88f, 0.12f, 0.56f), // Pink
            _ => new Color(0.58f, 0.63f, 0.67f)  // Slate
        };
    }

    // =========================================================================
    // ROBUST SERIALIZATION LAYER (USING NEWTONSOFT JSON WITH KEY FIX)
    // =========================================================================

    private void SaveLevelJson()
    {
        string path = EditorUtility.SaveFilePanel("Save Dampling Level", "", "NewLevel.json", "json");
        if (string.IsNullOrEmpty(path)) return;

        GameLevelSchema level = new GameLevelSchema
        {
            LevelId = 1,
            LevelName = Path.GetFileNameWithoutExtension(path),
            ConveyorBeltMaxCapacity = BELT_MAX,
            ResolutionQueues = generatedQueues
        };

        level.Grid.Columns = gridColumns;
        level.Grid.Rows = gridRows;

        level.Grid.Matrix = new List<GameLevelSchema.CellNode>();

        foreach (var kvp in editorMatrix)
        {
            var coord = new GameLevelSchema.Coordinate(kvp.Key.x, kvp.Key.y);
            var node = new GameLevelSchema.CellNode { Position = coord };

            Vector2Int belowKey = new Vector2Int(kvp.Key.x, kvp.Key.y + 1);
            bool hasPipeBelow = editorMatrix.ContainsKey(belowKey) && editorMatrix[belowKey].Behavior == CellBehavior.Pipe;

            if (kvp.Value.Behavior == CellBehavior.Blocker)
            {
                node.IsPlayablePath = false;
            }
            else if (kvp.Value.Behavior == CellBehavior.Pipe)
            {
                node.ContinuousPipe = new GameLevelSchema.PipeGenerator { MaxTotalEmissions = kvp.Value.PipeEmissions };

                for (int i = 0; i < kvp.Value.PipeEmissions; i++)
                {
                    var unit = new GameLevelSchema.GridUnit();
                    string color = !string.IsNullOrEmpty(kvp.Value.AssignedColorId) ? kvp.Value.AssignedColorId : "Color_0";
                    for (int d = 0; d < 9; d++) unit.InteriorContents.Add(new GameLevelSchema.DumplingItem { ColorId = color });
                    node.ContinuousPipe.ReservoirQueue.Add(unit);
                }
            }
            else if (kvp.Value.Behavior == CellBehavior.Standard)
            {
                if (!string.IsNullOrEmpty(kvp.Value.AssignedColorId))
                {
                    node.OccupyingUnit = new GameLevelSchema.GridUnit();
                    for (int d = 0; d < 9; d++)
                    {
                        node.OccupyingUnit.InteriorContents.Add(new GameLevelSchema.DumplingItem { ColorId = kvp.Value.AssignedColorId });
                        node.OccupyingUnit.IsHiddenUntilUnblocked = kvp.Value.StartHidden;
                    }

                    if (hasPipeBelow)
                    {
                        Guid fakePipeBlockerId = new Guid(kvp.Key.x, (short)(kvp.Key.y + 1), 0, 0, 0, 0, 0, 0, 0, 0, 0);
                        node.OccupyingUnit.ExplicitlyBlockedByUnitIds.Add(fakePipeBlockerId);
                    }
                }
            }

            level.Grid.Matrix.Add(node);
        }
        var settings = new Newtonsoft.Json.JsonSerializerSettings
        {
            Formatting = Newtonsoft.Json.Formatting.Indented,
            ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore
        };

        string json = Newtonsoft.Json.JsonConvert.SerializeObject(level, settings);
        File.WriteAllText(path, json);
        AssetDatabase.Refresh();
    }

    private void LoadLevelJson()
    {
        string path = EditorUtility.OpenFilePanel("Load Dampling Level", "", "json");
        if (string.IsNullOrEmpty(path)) return;

        string json = File.ReadAllText(path);

        var levelData = Newtonsoft.Json.JsonConvert.DeserializeObject<GameLevelSchema>(json);
        if (levelData == null) return;

        gridColumns = levelData.Grid.Columns;
        gridRows = levelData.Grid.Rows;
        queueCount = levelData.ResolutionQueues.Count;

        BuildCanvas();
        generatedQueues = levelData.ResolutionQueues;

        foreach (var cellNode in levelData.Grid.Matrix)
        {
            Vector2Int key = new Vector2Int(cellNode.Position.X, cellNode.Position.Y);
            if (!editorMatrix.ContainsKey(key)) continue;

            EditorCell cell = editorMatrix[key];

            if (!cellNode.IsPlayablePath)
            {
                cell.Behavior = CellBehavior.Blocker;
                cell.AssignedColorId = "";
            }
            else if (cellNode.ContinuousPipe != null)
            {
                cell.Behavior = CellBehavior.Pipe;
                cell.PipeEmissions = cellNode.ContinuousPipe.MaxTotalEmissions ?? 3;

                var firstUnit = cellNode.ContinuousPipe.ReservoirQueue.FirstOrDefault();
                cell.AssignedColorId = firstUnit?.InteriorContents.FirstOrDefault()?.ColorId ?? "";
            }
            else
            {
                cell.Behavior = CellBehavior.Standard;
                var firstItem = cellNode.OccupyingUnit?.InteriorContents.FirstOrDefault();
                cell.AssignedColorId = firstItem?.ColorId ?? "";
                cell.StartHidden = cellNode.OccupyingUnit?.IsHiddenUntilUnblocked ?? false;
            }
        }
        Repaint();
    }

    private GameLevelSchema AssembleActiveEditorStateToSchema()
    {
        GameLevelSchema level = new GameLevelSchema
        {
            LevelId = 1,
            LevelName = "Editor_Transient_Test",
            ConveyorBeltMaxCapacity = BELT_MAX,
            ResolutionQueues = generatedQueues
        };

        level.Grid.Columns = gridColumns;
        level.Grid.Rows = gridRows;

        foreach (var kvp in editorMatrix.OrderBy(p => p.Key.y).ThenBy(p => p.Key.x))
        {
            var coord = new GameLevelSchema.Coordinate(kvp.Key.x, kvp.Key.y);
            var node = new GameLevelSchema.CellNode { Position = coord };

            Vector2Int belowKey = new Vector2Int(kvp.Key.x, kvp.Key.y + 1);
            bool hasPipeBelow = editorMatrix.ContainsKey(belowKey) && editorMatrix[belowKey].Behavior == CellBehavior.Pipe;

            if (kvp.Value.Behavior == CellBehavior.Blocker)
            {
                node.IsPlayablePath = false;
            }
            else if (kvp.Value.Behavior == CellBehavior.Pipe)
            {
                node.ContinuousPipe = new GameLevelSchema.PipeGenerator { MaxTotalEmissions = kvp.Value.PipeEmissions };
                for (int i = 0; i < kvp.Value.PipeEmissions; i++)
                {
                    var unit = new GameLevelSchema.GridUnit();
                    string color = !string.IsNullOrEmpty(kvp.Value.AssignedColorId) ? kvp.Value.AssignedColorId : "Color_0";
                    for (int d = 0; d < 9; d++) unit.InteriorContents.Add(new GameLevelSchema.DumplingItem { ColorId = color });
                    node.ContinuousPipe.ReservoirQueue.Add(unit);
                }
            }
            else if (kvp.Value.Behavior == CellBehavior.Standard)
            {
                if (!string.IsNullOrEmpty(kvp.Value.AssignedColorId))
                {
                    node.OccupyingUnit = new GameLevelSchema.GridUnit();
                    for (int d = 0; d < 9; d++) node.OccupyingUnit.InteriorContents.Add(new GameLevelSchema.DumplingItem { ColorId = kvp.Value.AssignedColorId });
                    
                    if (hasPipeBelow)
                    {
                        Guid fakePipeBlockerId = new Guid(kvp.Key.x, (short)(kvp.Key.y + 1), 0, 0, 0, 0, 0, 0, 0, 0, 0);
                        node.OccupyingUnit.ExplicitlyBlockedByUnitIds.Add(fakePipeBlockerId);
                    }
                }
            }
            level.Grid.Matrix.Add(node);
        }
        return level;
    }

}