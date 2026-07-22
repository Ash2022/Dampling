using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

public class LevelEditorWindow : EditorWindow
{
    public const int BELT_MAX = 30;

    private bool isOddColumnsWidth = false; // False = 8x8 Grid, True = 7x8 Grid
    private bool hardLevel = false;
    private int gridColumns => isOddColumnsWidth ? 7 : 6;
    private int gridRows => 7; // Row sizing is now locked to 8 permanently

    private int queueCount = 4;
    private int colorCount = 4;

    private enum CellBehavior { Standard, Blocker, Pipe }

    private class EditorCell
    {
        public CellBehavior Behavior = CellBehavior.Standard;
        public int PipeEmissions = 3;
        public int AssignedColorIndex = -1;

        public List<int> PipeEmittedColorIndexes = new List<int>();
        public bool StartHidden = false;

        // Relationship Feature Group IDs (0 means unassigned)
        public int KeyGroupId = 0;
        public int LockGroupId = 0;
        public int LinkGroupId = 0;

        public int IceLayers = 0;
    }

    private Dictionary<Vector2Int, EditorCell> editorMatrix = new Dictionary<Vector2Int, EditorCell>();
    private List<List<GameLevelSchema.ContainerData>> generatedQueues = new List<List<GameLevelSchema.ContainerData>>();
    private bool isGridBuilt = false;

    // Linking and Chaining workflow tracking states
    private Vector2Int activeKeySource = new Vector2Int(-1, -1);
    private Vector2Int activeLinkSource = new Vector2Int(-1, -1);

    private const float CellSize = 75f;
    private const float Padding = 5f;
    private const float QueueWidth = 100f;
    private const float QueueContainerHeight = 24f;

    private string botReportOutputText = "";

    DamplingSimulationAgent botAgent;
    DamplingSimulationAgentSmart smartAgent;
    DamplingSimulationAgentGreedy greedyAgent;

    [MenuItem("Tools/Dampling Level Editor")]
    public static void ShowWindow()
    {
        GetWindow<LevelEditorWindow>("Dampling Editor");
    }

    private void OnGUI()
    {
        GUILayout.Label("Level Layout Configuration", EditorStyles.boldLabel);

        // --- DIMENSION CONTROLS TOGGLE ---
        EditorGUI.BeginChangeCheck();
        isOddColumnsWidth = EditorGUILayout.Toggle("Symmetry Mode (7x8 Odd)", isOddColumnsWidth);
        if (EditorGUI.EndChangeCheck())
        {
            BuildCanvas();
        }
        hardLevel = EditorGUILayout.Toggle("Hard Level", hardLevel);
        EditorGUILayout.LabelField($"Active Workspace Dimensions: {gridColumns} Columns × {gridRows} Rows");

        queueCount = EditorGUILayout.IntSlider("Container Queues", queueCount, 1, 8);
        colorCount = EditorGUILayout.IntSlider("Number of Colors", colorCount, 2, 8);

        EditorGUILayout.Space();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("1. Build Canvas Structure", GUILayout.Height(30))) { BuildCanvas(); }
        EditorGUI.BeginDisabledGroup(!isGridBuilt);
        if (GUILayout.Button("2. Populate Balance (Zero-Sum)", GUILayout.Height(30))) { PopulateLevelData(); }
        if (GUILayout.Button("2. Randomize Containers Only", GUILayout.Height(30))) { PopulateLevelData(true); }
        if (GUILayout.Button("Reset / Clear Data", GUILayout.Height(30))) { ClearPopulatedData(); }
        EditorGUI.EndDisabledGroup();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save Level JSON")) { SaveLevelJson(); }
        if (GUILayout.Button("Load Level JSON")) { LoadLevelJson(); }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Validate Level", GUILayout.Height(30)))
            ValidateSupplyAndDemand();

        if (GUILayout.Button("Run Bot Simulation (1000)", GUILayout.Height(30)))
            RunEditorSimulation();

        if (GUILayout.Button("Run Smart Bot Simulation (1000)", GUILayout.Height(30)))
            RunEditorSimulationSmartAgent();

        if (GUILayout.Button("Run Greedy Simulation (1000)", GUILayout.Height(30)))
            RunEditorSimulationGreedyAgent();

        GUILayout.EndHorizontal();
        EditorGUILayout.Space();

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
                editorMatrix[new Vector2Int(x, y)] = new EditorCell { Behavior = CellBehavior.Blocker };
            }
        }

        for (int i = 0; i < queueCount; i++)
        {
            generatedQueues.Add(new List<GameLevelSchema.ContainerData>());
        }

        isGridBuilt = true;
        activeKeySource = new Vector2Int(-1, -1);
        activeLinkSource = new Vector2Int(-1, -1);
    }

    private void ClearPopulatedData()
    {
        foreach (var cell in editorMatrix.Values)
        {
            cell.Behavior = CellBehavior.Standard;
            cell.PipeEmissions = 3;
            cell.AssignedColorIndex = -1;
            cell.StartHidden = false;
            cell.KeyGroupId = 0;
            cell.LockGroupId = 0;
            cell.LinkGroupId = 0;
            cell.IceLayers = 0;
            cell.PipeEmittedColorIndexes.Clear();
        }

        foreach (var q in generatedQueues) q.Clear();
        activeKeySource = new Vector2Int(-1, -1);
        activeLinkSource = new Vector2Int(-1, -1);
        botReportOutputText = "";
        Repaint();
    }

    private void PopulateLevelData(bool keepUnits = false)
    {
        // 1. Gather existing unit colors (only if keeping, otherwise we assign them)
        List<int> currentUnitColors = new List<int>();

        if (!keepUnits)
        {
            // ... (Your original logic to generate unitColorAssignments) ...
            int totalUnits = 0;
            foreach (var cell in editorMatrix.Values)
            {
                if (cell.Behavior == CellBehavior.Standard || cell.Behavior == CellBehavior.Pipe)
                    totalUnits += 1;
            }
            if (totalUnits == 0) return;

            List<int> colorPalette = new List<int>();
            for (int i = 0; i < colorCount; i++) colorPalette.Add(i);

            List<int> unitColorAssignments = new List<int>();
            for (int i = 0; i < totalUnits; i++) unitColorAssignments.Add(colorPalette[i % colorPalette.Count]);

            System.Random rnd = new System.Random();
            unitColorAssignments = unitColorAssignments.OrderBy(x => rnd.Next()).ToList();

            // Assign to cells
            int assignedCount = 0;
            foreach (var cell in editorMatrix.Values)
            {
                if (cell.Behavior == CellBehavior.Standard && assignedCount < unitColorAssignments.Count)
                {
                    cell.AssignedColorIndex = unitColorAssignments[assignedCount++];
                }
                else if (cell.Behavior == CellBehavior.Pipe && assignedCount < unitColorAssignments.Count)
                {
                    cell.PipeEmittedColorIndexes.Clear();

                    // Take exactly one valid color assignment for its active unit
                    int chosenColor = unitColorAssignments[assignedCount++];
                    cell.AssignedColorIndex = chosenColor;
                    cell.PipeEmittedColorIndexes.Add(chosenColor);

                    // Fill any remaining emissions with matching colors to prevent sub-system crashes
                    for (int e = 1; e < cell.PipeEmissions; e++)
                    {
                        cell.PipeEmittedColorIndexes.Add(chosenColor);
                    }
                }
                else
                {
                    cell.AssignedColorIndex = -1;
                    cell.PipeEmittedColorIndexes.Clear();
                }
            }
            currentUnitColors = unitColorAssignments;
        }
        else
        {
            // Extract what is currently in the matrix
            foreach (var cell in editorMatrix.Values)
            {
                if (cell.Behavior == CellBehavior.Standard && cell.AssignedColorIndex != -1)
                    currentUnitColors.Add(cell.AssignedColorIndex);
                else if (cell.Behavior == CellBehavior.Pipe)
                    currentUnitColors.AddRange(cell.PipeEmittedColorIndexes);
            }
        }

        // 2. Refresh Containers (This part runs regardless of keepUnits)
        System.Random rndPool = new System.Random();
        List<GameLevelSchema.ContainerData> globalContainerPool = new List<GameLevelSchema.ContainerData>();

        foreach (var color in currentUnitColors)
        {
            globalContainerPool.Add(new GameLevelSchema.ContainerData { ColorIndex = color });
            globalContainerPool.Add(new GameLevelSchema.ContainerData { ColorIndex = color });
            globalContainerPool.Add(new GameLevelSchema.ContainerData { ColorIndex = color });
        }
        globalContainerPool = globalContainerPool.OrderBy(x => rndPool.Next()).ToList();

        foreach (var q in generatedQueues) q.Clear();
        for (int i = 0; i < globalContainerPool.Count; i++)
        {
            generatedQueues[i % queueCount].Add(globalContainerPool[i]);
        }
    }

    private void DrawEditorWorkspaceSideBySide()
    {
        Event e = Event.current;
        GUILayout.BeginHorizontal();

        // --- LEFT PANEL: SUPPLY GRID DRAWING LAYER ---
        GUILayout.BeginVertical();
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

                Vector2Int belowCoord = new Vector2Int(x, y + 1);
                bool isTargetedByPipeBelow = editorMatrix.ContainsKey(belowCoord) && editorMatrix[belowCoord].Behavior == CellBehavior.Pipe;

                Color displayColor = cell.Behavior switch
                {
                    CellBehavior.Blocker => new Color(0.25f, 0.25f, 0.25f),
                    CellBehavior.Pipe => new Color(0.18f, 0.44f, 0.72f),
                    _ => isTargetedByPipeBelow ? new Color(0.25f, 0.55f, 0.85f, 0.4f) :
                         cell.IceLayers > 0 ? new Color(0.6f, 0.85f, 0.95f) :
                         cell.AssignedColorIndex != -1 ? GetColorValue(cell.AssignedColorIndex) :
                         new Color(0.85f, 0.85f, 0.85f)
                };

                EditorGUI.DrawRect(cellRect, displayColor);

                bool hasColor = cell.AssignedColorIndex != -1 && cell.Behavior != CellBehavior.Blocker;
                GUIStyle textStyle = hasColor ? EditorStyles.whiteMiniLabel : EditorStyles.centeredGreyMiniLabel;

                if (cell.Behavior == CellBehavior.Standard)
                {
                    string pipeTargetLabel = isTargetedByPipeBelow ? " [Outlet]" : "";
                    string contentLabel = hasColor ? $"({x},{y}){pipeTargetLabel}\nColor_{cell.AssignedColorIndex}" : $"({x},{y}){pipeTargetLabel}\nUnit";
                    GUI.Label(cellRect, contentLabel, textStyle);

                    string relationOverlay = "";
                    if (cell.KeyGroupId > 0) relationOverlay += $"[KEY_{cell.KeyGroupId}] ";
                    if (cell.LockGroupId > 0) relationOverlay += $"[LOCK_{cell.LockGroupId}] ";
                    if (cell.LinkGroupId > 0) relationOverlay += $"[L_{cell.LinkGroupId}] ";

                    if (!string.IsNullOrEmpty(relationOverlay))
                    {
                        Rect tagRect = new Rect(tagRect = new Rect(cellRect.x + 2, cellRect.y + 20, cellRect.width - 4, 16f));
                        EditorGUI.DrawRect(tagRect, new Color(0.1f, 0.1f, 0.1f, 0.85f));
                        GUI.Label(tagRect, relationOverlay, EditorStyles.whiteMiniLabel);
                    }

                    if (cell.IceLayers > 0)
                    {
                        Rect iceRect = new Rect(cellRect.x + 2, cellRect.y + cellRect.height - 36, cellRect.width - 4, 16f);
                        EditorGUI.DrawRect(iceRect, new Color(0f, 0.3f, 0.5f, 0.9f));
                        GUI.Label(new Rect(iceRect.x + 2, iceRect.y, 35, 16), "Ice:", EditorStyles.whiteMiniLabel);
                        cell.IceLayers = EditorGUI.IntField(new Rect(iceRect.x + 28, iceRect.y, iceRect.width - 30, 16), cell.IceLayers);
                    }

                    if (cell.StartHidden && hasColor)
                    {
                        Rect hiddenIndicatorRect = new Rect(cellRect.x + 2, cellRect.y + cellRect.height - 18, cellRect.width - 4, 16f);
                        EditorGUI.DrawRect(hiddenIndicatorRect, new Color(0f, 0f, 0f, 0.6f));
                        GUI.Label(hiddenIndicatorRect, "HIDDEN STATE", EditorStyles.whiteMiniLabel);
                    }
                }
                else if (cell.Behavior == CellBehavior.Blocker)
                {
                    GUI.Label(new Rect(cellRect.x + 4, cellRect.y + 28, cellRect.width - 8, 16), "BLOCKER", EditorStyles.whiteMiniLabel);
                }
                else if (cell.Behavior == CellBehavior.Pipe)
                {
                    Rect labelRect = new Rect(cellRect.x, cellRect.y + 2, cellRect.width, 16f);
                    string pipeLabelText = hasColor ? $"Pipe: Color_{cell.AssignedColorIndex}" : "Pipe Anchor";
                    GUI.Label(labelRect, pipeLabelText, EditorStyles.whiteMiniLabel);

                    Rect inputRect = new Rect(cellRect.x + 6, cellRect.y + 20, cellRect.width - 12, 18f);

                    EditorGUI.BeginChangeCheck();
                    int newEmissions = EditorGUI.IntField(inputRect, cell.PipeEmissions);
                    if (EditorGUI.EndChangeCheck())
                    {
                        cell.PipeEmissions = newEmissions;

                        // Ensure the emitted colors list matches the emission count
                        while (cell.PipeEmittedColorIndexes.Count < cell.PipeEmissions)
                            cell.PipeEmittedColorIndexes.Add(cell.AssignedColorIndex != -1 ? cell.AssignedColorIndex : 0);
                        while (cell.PipeEmittedColorIndexes.Count > cell.PipeEmissions)
                            cell.PipeEmittedColorIndexes.RemoveAt(cell.PipeEmittedColorIndexes.Count - 1);

                        // Sync the master assigned color to the first emission if available
                        if (cell.PipeEmittedColorIndexes.Count > 0)
                            cell.AssignedColorIndex = cell.PipeEmittedColorIndexes[0];
                    }

                    // Draw small color rects
                    float pipSize = 15f;
                    float paddingX = 2f;
                    float startX = cellRect.x + 2f;
                    float startY = cellRect.y + 40f;

                    int cols = (int)(cellRect.width / (pipSize + paddingX));

                    for (int i = 0; i < cell.PipeEmittedColorIndexes.Count; i++)
                    {
                        int row = i / cols;
                        int col = i % cols;

                        float pX = startX + (col * (pipSize + paddingX));
                        float pY = startY + (row * (pipSize + 2f));

                        Rect pipRect = new Rect(pX, pY, pipSize, pipSize);

                        if (pipRect.Contains(e.mousePosition) && e.type == EventType.MouseDown && e.button == 0)
                        {
                            cell.PipeEmittedColorIndexes[i] = GetNextColorIndex(cell.PipeEmittedColorIndexes[i], colorCount);

                            // If the first pip is changed, update the primary AssignedColorIndex so the tool logic syncs
                            if (i == 0) cell.AssignedColorIndex = cell.PipeEmittedColorIndexes[0];

                            e.Use();
                            Repaint();
                        }

                        EditorGUI.DrawRect(pipRect, GetColorValue(cell.PipeEmittedColorIndexes[i]));
                    }
                }

                // --- INSTANT MIDDLE-CLICK PAINTING ENGINE ---
                if (cellRect.Contains(e.mousePosition) && e.type == EventType.MouseDown && e.button == 2)
                {
                    if (cell.Behavior == CellBehavior.Blocker)
                    {
                        cell.Behavior = CellBehavior.Standard;
                    }
                    else
                    {
                        ResetRelations(cell);
                        cell.Behavior = CellBehavior.Blocker;
                        cell.AssignedColorIndex = -1;
                        cell.StartHidden = false;
                        cell.IceLayers = 0;
                        cell.PipeEmittedColorIndexes.Clear();
                    }

                    e.Use();
                    Repaint();
                }
                else if (cellRect.Contains(e.mousePosition) && e.type == EventType.MouseDown && e.button == 0)
                {
                    cell.AssignedColorIndex = GetNextColorIndex(cell.AssignedColorIndex, colorCount);
                    e.Use();
                    Repaint();
                }


                // Interactive Context Configuration Processing (Right-Click Controls)
                if (cellRect.Contains(e.mousePosition) && e.type == EventType.ContextClick)
                {
                    GenericMenu menu = new GenericMenu();

                    if (cell.Behavior == CellBehavior.Standard)
                    {
                        menu.AddItem(new GUIContent("Environment/Add Ice Layer"), cell.IceLayers > 0, () => { if (cell.IceLayers == 0) cell.IceLayers = 1; Repaint(); });
                        menu.AddItem(new GUIContent("Environment/Thaw Ice Completely"), cell.IceLayers == 0, () => { cell.IceLayers = 0; Repaint(); });
                        menu.AddSeparator("Environment/");

                        if (cell.AssignedColorIndex != -1)
                        {
                            menu.AddItem(new GUIContent("Unit/Start Revealed"), !cell.StartHidden, () => { cell.StartHidden = false; Repaint(); });
                            menu.AddItem(new GUIContent("Unit/Start Hidden"), cell.StartHidden, () => { cell.StartHidden = true; Repaint(); });
                            menu.AddSeparator("Unit/");

                            if (cell.LinkGroupId == 0)
                            {
                                int nextKeyId = GetNextAvailableKeyGroupId();
                                menu.AddItem(new GUIContent($"Chains/Set as Key_{nextKeyId}"), false, () => { cell.KeyGroupId = nextKeyId; activeKeySource = coord; Repaint(); });

                                if (activeKeySource != new Vector2Int(-1, -1) && activeKeySource != coord)
                                {
                                    int pendingKeyNum = editorMatrix[activeKeySource].KeyGroupId;
                                    menu.AddItem(new GUIContent($"Chains/Connect as Lock for Key_{pendingKeyNum}"), false, () => { cell.LockGroupId = pendingKeyNum; activeKeySource = new Vector2Int(-1, -1); Repaint(); });
                                }
                            }

                            if (cell.KeyGroupId == 0 && cell.LockGroupId == 0)
                            {
                                int nextLinkId = GetNextAvailableLinkGroupId();
                                menu.AddItem(new GUIContent($"Linking/Link From Here... (L_{nextLinkId})"), false, () => { cell.LinkGroupId = nextLinkId; activeLinkSource = coord; Repaint(); });

                                if (activeLinkSource != new Vector2Int(-1, -1) && activeLinkSource != coord)
                                {
                                    int pendingLinkNum = editorMatrix[activeLinkSource].LinkGroupId;
                                    menu.AddItem(new GUIContent($"Linking/Complete Link to Group L_{pendingLinkNum}"), false, () => { cell.LinkGroupId = pendingLinkNum; activeLinkSource = new Vector2Int(-1, -1); Repaint(); });
                                }
                            }

                            menu.AddItem(new GUIContent("Relations/Clear All Links & Chains"), false, () => { cell.KeyGroupId = 0; cell.LockGroupId = 0; cell.LinkGroupId = 0; Repaint(); });
                            menu.AddSeparator("");
                            menu.AddItem(new GUIContent("Convert Cell/To Blocker (Hole)"), false, () => { ResetRelations(cell); cell.Behavior = CellBehavior.Blocker; cell.AssignedColorIndex = -1; cell.StartHidden = false; cell.IceLayers = 0; Repaint(); });
                            menu.AddItem(new GUIContent("Convert Cell/To Pipe Generator"), false, () => { ResetRelations(cell); cell.Behavior = CellBehavior.Pipe; cell.AssignedColorIndex = -1; cell.StartHidden = false; cell.IceLayers = 0; Repaint(); });
                        }
                    }
                    else
                    {
                        menu.AddItem(new GUIContent("Set Standard Unit"), cell.Behavior == CellBehavior.Standard, () => { cell.Behavior = CellBehavior.Standard; cell.AssignedColorIndex = -1; cell.StartHidden = false; Repaint(); });
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

        // --- RIGHT PANEL: RESOLUTION QUEUES DRAWING LAYER ---
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

                if (containerRect.Contains(e.mousePosition) && e.type == EventType.MouseDown && e.button == 0)
                {
                    // Cycle the color for this specific container in the queue
                    int currentIndex = generatedQueues[q][c].ColorIndex;
                    generatedQueues[q][c].ColorIndex = GetNextColorIndex(currentIndex, colorCount);

                    e.Use();
                    Repaint();
                }

                int colorIndex = generatedQueues[q][c].ColorIndex;

                EditorGUI.DrawRect(containerRect, GetColorValue(colorIndex));
                GUIStyle labelOverride = new GUIStyle(EditorStyles.whiteBoldLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
                EditorGUI.LabelField(containerRect, $"COLOR_{colorIndex}", labelOverride);
            }
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    private int GetNextAvailableKeyGroupId()
    {
        int max = 0;
        foreach (var cell in editorMatrix.Values) max = Mathf.Max(max, cell.KeyGroupId);
        return max + 1;
    }

    private int GetNextAvailableLinkGroupId()
    {
        int max = 0;
        foreach (var cell in editorMatrix.Values) max = Mathf.Max(max, cell.LinkGroupId);
        return max + 1;
    }

    private void ResetRelations(EditorCell cell)
    {
        cell.KeyGroupId = 0;
        cell.LockGroupId = 0;
        cell.LinkGroupId = 0;
    }

    private Color GetColorValue(int colorIndex)
    {
        if (colorIndex < 0) return Color.white;

        int index = colorIndex;
        return index switch
        {
            0 => new Color(0.85f, 0.23f, 0.23f),
            1 => new Color(0.18f, 0.67f, 0.18f),
            2 => new Color(0.14f, 0.38f, 0.78f),
            3 => new Color(0.88f, 0.72f, 0.12f),
            4 => new Color(0.53f, 0.18f, 0.68f),
            5 => new Color(0.12f, 0.72f, 0.72f),
            6 => new Color(0.88f, 0.45f, 0.12f),
            7 => new Color(0.44f, 0.26f, 0.12f),
            8 => new Color(0.88f, 0.12f, 0.56f),
            _ => new Color(0.58f, 0.63f, 0.67f)
        };
    }

    private void SaveLevelJson()
    {
        string path = EditorUtility.SaveFilePanel("Save Dampling Level", "", "NewLevel.json", "json");
        if (string.IsNullOrEmpty(path)) return;

        GameLevelSchema level = AssembleActiveEditorStateToSchema();
        level.LevelName = Path.GetFileNameWithoutExtension(path);

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

        int loadedCols = levelData.Grid.Columns;
        isOddColumnsWidth = (loadedCols == 7);

        queueCount = levelData.ResolutionQueues.Count;

        BuildCanvas();
        generatedQueues = levelData.ResolutionQueues;
        hardLevel = levelData.HardLevel;

        Dictionary<int, Vector2Int> guidToCoordMap = new Dictionary<int, Vector2Int>();
        foreach (var cellNode in levelData.Grid.Matrix)
        {
            if (cellNode.OccupyingUnit != null)
            {
                guidToCoordMap[cellNode.OccupyingUnit.UnitId] = new Vector2Int(cellNode.Position.X, cellNode.Position.Y);
            }
        }

        int keyGroupCounter = 1;
        int linkGroupCounter = 1;

        foreach (var cellNode in levelData.Grid.Matrix)
        {
            Vector2Int key = new Vector2Int(cellNode.Position.X, cellNode.Position.Y);
            if (!editorMatrix.ContainsKey(key)) continue;

            EditorCell cell = editorMatrix[key];

            if (!cellNode.IsPlayablePath)
            {
                cell.Behavior = CellBehavior.Blocker;
                cell.AssignedColorIndex = -1;
                cell.IceLayers = 0;
            }
            else if (cellNode.ContinuousPipe != null)
            {
                cell.Behavior = CellBehavior.Pipe;
                cell.PipeEmissions = cellNode.ContinuousPipe.MaxTotalEmissions ?? 3;
                cell.IceLayers = 0;
                cell.PipeEmittedColorIndexes.Clear();

                if (cellNode.ContinuousPipe.ReservoirQueue != null)
                {
                    foreach (var unit in cellNode.ContinuousPipe.ReservoirQueue)
                    {
                        int unitColorIndex = unit.InteriorContents.FirstOrDefault()?.ColorIndex ?? 0;
                        cell.PipeEmittedColorIndexes.Add(unitColorIndex);
                    }
                }
                var firstUnit = cellNode.ContinuousPipe.ReservoirQueue.FirstOrDefault();
                cell.AssignedColorIndex = firstUnit?.InteriorContents.FirstOrDefault()?.ColorIndex ?? -1;
            }
            else
            {
                cell.Behavior = CellBehavior.Standard;
                var firstItem = cellNode.OccupyingUnit?.InteriorContents.FirstOrDefault();
                // This handles loading old JSONs with string ColorId
                var colorIdString = firstItem?.ColorIndex;
                cell.AssignedColorIndex = firstItem?.ColorIndex ?? -1;
                cell.StartHidden = cellNode.OccupyingUnit?.IsHiddenUntilUnblocked ?? false;
                cell.IceLayers = cellNode.OccupyingUnit?.IceLayers ?? 0;

                if (cellNode.OccupyingUnit != null)
                {
                    if (cellNode.OccupyingUnit.LinkedUnitIds.Count > 0 && cell.LinkGroupId == 0)
                    {
                        int targetLinkId = linkGroupCounter++;
                        cell.LinkGroupId = targetLinkId;
                        foreach (var linkedGuid in cellNode.OccupyingUnit.LinkedUnitIds)
                        {
                            if (guidToCoordMap.TryGetValue(linkedGuid, out Vector2Int linkedCoord))
                            {
                                editorMatrix[linkedCoord].LinkGroupId = targetLinkId;
                            }
                        }
                    }

                    if (cellNode.OccupyingUnit.ExplicitlyBlockedByUnitIds.Count > 0)
                    {
                        foreach (var blockerGuid in cellNode.OccupyingUnit.ExplicitlyBlockedByUnitIds)
                        {
                            if (guidToCoordMap.TryGetValue(blockerGuid, out Vector2Int keyCoord))
                            {
                                EditorCell keyCell = editorMatrix[keyCoord];
                                if (keyCell.KeyGroupId == 0)
                                {
                                    keyCell.KeyGroupId = keyGroupCounter++;
                                }
                                cell.LockGroupId = keyCell.KeyGroupId;
                            }
                        }
                    }
                }
            }
        }
        Repaint();
    }

    private GameLevelSchema AssembleActiveEditorStateToSchema()
    {
        int nextContainerId = 0;
        if (generatedQueues != null)
        {
            foreach (var lane in generatedQueues)
            {
                if (lane != null)
                {
                    foreach (var container in lane)
                    {
                        if (container != null)
                        {
                            container.Id = nextContainerId++;
                        }
                    }
                }
            }
        }

        GameLevelSchema level = new GameLevelSchema
        {
            LevelId = 1,
            LevelName = "Editor_Transient_State",
            ConveyorBeltMaxCapacity = BELT_MAX,
            ResolutionQueues = generatedQueues
        };

        level.Grid.Columns = gridColumns;
        level.Grid.Rows = gridRows;
        level.Grid.Matrix = new List<GameLevelSchema.CellNode>();
        level.HardLevel = hardLevel;

        Dictionary<Vector2Int, int> positionToIdMap = new Dictionary<Vector2Int, int>();
        int nextUnitId = 0;

        var sortedCoordinates = editorMatrix.OrderBy(p => p.Key.y).ThenBy(p => p.Key.x).ToList();

        foreach (var kvp in sortedCoordinates)
        {
            if (kvp.Value.Behavior != CellBehavior.Blocker)
            {
                positionToIdMap[kvp.Key] = nextUnitId++;
            }
            else
            {
                positionToIdMap[kvp.Key] = -1;
            }
        }

        foreach (var kvp in sortedCoordinates)
        {
            var coord = new GameLevelSchema.Coordinate(kvp.Key.x, kvp.Key.y);
            var node = new GameLevelSchema.CellNode { Position = coord };

            if (kvp.Value.Behavior == CellBehavior.Blocker)
            {
                node.IsPlayablePath = false;
            }
            else if (kvp.Value.Behavior == CellBehavior.Pipe)
            {
                node.IsPlayablePath = true;
                node.ContinuousPipe = new GameLevelSchema.PipeGenerator { MaxTotalEmissions = kvp.Value.PipeEmissions };

                if (node.ContinuousPipe.ReservoirQueue == null)
                {
                    node.ContinuousPipe.ReservoirQueue = new List<GameLevelSchema.GridUnit>();
                }

                for (int i = 0; i < kvp.Value.PipeEmissions; i++)
                {
                    int uniqueUnitId = (positionToIdMap[kvp.Key] * 100) + i;

                    var unit = new GameLevelSchema.GridUnit
                    {
                        UnitId = uniqueUnitId,
                        IceLayers = 0,
                        ExplicitlyBlockedByUnitIds = new List<int>(),
                        LinkedUnitIds = new List<int>(),
                        IsHiddenUntilUnblocked = false
                    };

                    int colorIndex = 0;
                    if (kvp.Value.PipeEmittedColorIndexes != null && i < kvp.Value.PipeEmittedColorIndexes.Count)
                    {
                        colorIndex = kvp.Value.PipeEmittedColorIndexes[i];
                    }
                    else if (kvp.Value.AssignedColorIndex != -1)
                    {
                        colorIndex = kvp.Value.AssignedColorIndex;
                    }

                    for (int d = 0; d < 9; d++)
                    {
                        unit.InteriorContents.Add(new GameLevelSchema.DumplingItem { ColorIndex = colorIndex });
                    }

                    node.ContinuousPipe.ReservoirQueue.Add(unit);
                }
            }
            else if (kvp.Value.Behavior == CellBehavior.Standard)
            {
                node.IsPlayablePath = true;

                if (kvp.Value.AssignedColorIndex != -1)
                {
                    node.OccupyingUnit = new GameLevelSchema.GridUnit
                    {
                        UnitId = positionToIdMap[kvp.Key],
                        IceLayers = kvp.Value.IceLayers
                    };
                    node.OccupyingUnit.IsHiddenUntilUnblocked = kvp.Value.StartHidden;

                    for (int d = 0; d < 9; d++)
                    {
                        node.OccupyingUnit.InteriorContents.Add(new GameLevelSchema.DumplingItem { ColorIndex = kvp.Value.AssignedColorIndex });
                    }

                    if (kvp.Value.LockGroupId > 0)
                    {
                        foreach (var cellPair in editorMatrix)
                        {
                            if (cellPair.Value.KeyGroupId == kvp.Value.LockGroupId)
                            {
                                node.OccupyingUnit.ExplicitlyBlockedByUnitIds.Add(positionToIdMap[cellPair.Key]);
                            }
                        }
                    }

                    if (kvp.Value.LinkGroupId > 0)
                    {
                        foreach (var cellPair in editorMatrix)
                        {
                            if (cellPair.Key != kvp.Key && cellPair.Value.LinkGroupId == kvp.Value.LinkGroupId)
                            {
                                node.OccupyingUnit.LinkedUnitIds.Add(positionToIdMap[cellPair.Key]);
                            }
                        }
                    }
                }
            }
            level.Grid.Matrix.Add(node);
        }
        return level;
    }

    private void RunEditorSimulation()
    {
        GameLevelSchema level = AssembleActiveEditorStateToSchema();
        if (level == null) return;

        if (botAgent == null) botAgent = new DamplingSimulationAgent();
        var report = botAgent.RunBatchSimulation(level, 1000);
        botAgent.GenerateTextSummary(report);
        string reportSummary = string.Join("\n", report.SummaryLog);
        EditorUtility.DisplayDialog($"Simulation Results: Level {level.LevelId}", reportSummary, "Close");
    }

    private void RunEditorSimulationSmartAgent()
    {
        GameLevelSchema level = AssembleActiveEditorStateToSchema();
        if (level == null) return;

        if (smartAgent == null) smartAgent = new DamplingSimulationAgentSmart();
        var report = smartAgent.RunBatchSimulation(level, 1000);
        smartAgent.GenerateTextSummary(report);
        string reportSummary = string.Join("\n", report.SummaryLog);
        EditorUtility.DisplayDialog($"Simulation Results: Level {level.LevelId}", reportSummary, "Close");
    }

    private void RunEditorSimulationGreedyAgent()
    {
        GameLevelSchema level = AssembleActiveEditorStateToSchema();
        if (level == null) return;

        if (greedyAgent == null) greedyAgent = new DamplingSimulationAgentGreedy();
        var report = greedyAgent.RunBatchSimulation(level, 1000);
        greedyAgent.GenerateTextSummary(report);
        string reportSummary = string.Join("\n", report.SummaryLog);
        EditorUtility.DisplayDialog($"Simulation Results: Level {level.LevelId}", reportSummary, "Close");
    }

    private bool ValidateSupplyAndDemand()
    {
        GameLevelSchema level = AssembleActiveEditorStateToSchema();
        if (level == null) return false;

        Dictionary<int, int> supplyCounts = new Dictionary<int, int>();
        foreach (var node in level.Grid.Matrix)
        {
            if (node.OccupyingUnit != null && node.OccupyingUnit.InteriorContents != null)
            {
                foreach (var item in node.OccupyingUnit.InteriorContents)
                {
                    if (!supplyCounts.ContainsKey(item.ColorIndex)) supplyCounts[item.ColorIndex] = 0;
                    supplyCounts[item.ColorIndex]++;
                }
            }

            if (node.ContinuousPipe != null && node.ContinuousPipe.ReservoirQueue != null)
            {
                foreach (var queuedUnit in node.ContinuousPipe.ReservoirQueue)
                {
                    if (queuedUnit.InteriorContents != null)
                    {
                        foreach (var item in queuedUnit.InteriorContents)
                        {
                            if (!supplyCounts.ContainsKey(item.ColorIndex)) supplyCounts[item.ColorIndex] = 0;
                            supplyCounts[item.ColorIndex]++;
                        }
                    }
                }
            }
        }

        Dictionary<int, int> demandCounts = new Dictionary<int, int>();
        if (level.ResolutionQueues != null)
        {
            foreach (var queue in level.ResolutionQueues)
            {
                foreach (var container in queue)
                {
                    if (!demandCounts.ContainsKey(container.ColorIndex)) demandCounts[container.ColorIndex] = 0;
                    demandCounts[container.ColorIndex] += container.Capacity;
                }
            }
        }

        HashSet<int> allColors = new HashSet<int>(supplyCounts.Keys.Concat(demandCounts.Keys));
        List<string> errors = new List<string>();

        foreach (var color in allColors)
        {
            int supply = supplyCounts.ContainsKey(color) ? supplyCounts[color] : 0;
            int demand = demandCounts.ContainsKey(color) ? demandCounts[color] : 0;
            if (supply != demand) errors.Add($"[{color}] Mismatch! Board Supply: {supply} vs Demand: {demand}.");
        }

        if (errors.Count > 0)
        {
            Debug.LogError($"<color=red><b>LEVEL VALIDATION FAILED!</b></color>\n{string.Join("\n", errors)}");
            EditorUtility.DisplayDialog("Validation Error", "Supply and demand do not match!", "OK");
            return false;
        }

        EditorUtility.DisplayDialog("Validation Success", "All dumplings on the board match perfectly!", "Excellent");
        return true;
    }

    private int GetNextColorIndex(int currentIndex, int colorCount)
    {
        if (colorCount <= 0) return 0;

        // If current is -1 (unassigned), start at 0. Otherwise, increment and wrap.
        int nextIndex = (currentIndex == -1) ? 0 : (currentIndex + 1) % colorCount;

        return nextIndex;
    }
}