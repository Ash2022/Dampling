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

    private int gridColumns = 6;
    private int gridRows = 8;
    private int queueCount = 4;
    private int colorCount = 4;

    private enum CellBehavior { Standard, Blocker, Pipe }
    
    private class EditorCell
    {
        public CellBehavior Behavior = CellBehavior.Standard;
        public int PipeEmissions = 3;
        public string AssignedColorId = ""; 
        public bool StartHidden = false;

        // Relationship Feature Group IDs (0 means unassigned)
        public int KeyGroupId = 0;
        public int LockGroupId = 0;
        public int LinkGroupId = 0;
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

    [MenuItem("Tools/Dampling Level Editor")]
    public static void ShowWindow()
    {
        GetWindow<LevelEditorWindow>("Dampling Editor");
    }

    private void OnGUI()
    {
        GUILayout.Label("Level Layout Configuration", EditorStyles.boldLabel);

        gridColumns = EditorGUILayout.IntSlider("Grid Columns (X)", gridColumns, 1, 15);
        gridRows = EditorGUILayout.IntSlider("Grid Rows (Y)", gridRows, 1, 20);
        queueCount = EditorGUILayout.IntSlider("Container Queues", queueCount, 1, 8);
        colorCount = EditorGUILayout.IntSlider("Number of Colors", colorCount, 2, 10);

        EditorGUILayout.Space();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("1. Build Canvas Structure", GUILayout.Height(30))) { BuildCanvas(); }
        EditorGUI.BeginDisabledGroup(!isGridBuilt);
        if (GUILayout.Button("2. Populate Balance (Zero-Sum)", GUILayout.Height(30))) { PopulateLevelData(); }
        if (GUILayout.Button("Reset / Clear Data", GUILayout.Height(30))) { ClearPopulatedData(); }
        EditorGUI.EndDisabledGroup();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save Level JSON")) { SaveLevelJson(); }
        if (GUILayout.Button("Load Level JSON")) { LoadLevelJson(); }
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
                editorMatrix[new Vector2Int(x, y)] = new EditorCell();
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
            cell.AssignedColorId = "";
            cell.StartHidden = false;
            cell.KeyGroupId = 0;
            cell.LockGroupId = 0;
            cell.LinkGroupId = 0;
        }

        foreach (var q in generatedQueues) q.Clear();
        activeKeySource = new Vector2Int(-1, -1);
        activeLinkSource = new Vector2Int(-1, -1);
        botReportOutputText = "";
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

        if (totalUnits == 0) return;

        List<string> colorPalette = new List<string>();
        for (int i = 0; i < colorCount; i++) colorPalette.Add($"Color_{i}");

        List<string> unitColorAssignments = new List<string>();
        for (int i = 0; i < totalUnits; i++) unitColorAssignments.Add(colorPalette[i % colorPalette.Count]);

        System.Random rnd = new System.Random();
        unitColorAssignments = unitColorAssignments.OrderBy(x => rnd.Next()).ToList();

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
                         !string.IsNullOrEmpty(cell.AssignedColorId) ? GetColorValue(cell.AssignedColorId) :
                         new Color(0.85f, 0.85f, 0.85f)
                };

                EditorGUI.DrawRect(cellRect, displayColor);

                bool hasColor = !string.IsNullOrEmpty(cell.AssignedColorId) && cell.Behavior != CellBehavior.Blocker;
                GUIStyle textStyle = hasColor ? EditorStyles.whiteMiniLabel : EditorStyles.centeredGreyMiniLabel;

                if (cell.Behavior == CellBehavior.Standard)
                {
                    string pipeTargetLabel = isTargetedByPipeBelow ? " [Outlet]" : "";
                    string contentLabel = hasColor ? $"({x},{y}){pipeTargetLabel}\n{cell.AssignedColorId}" : $"({x},{y}){pipeTargetLabel}\nUnit";
                    GUI.Label(cellRect, contentLabel, textStyle);

                    // Dynamic Tag Text Overlays for Relations Layouts
                    string relationOverlay = "";
                    if (cell.KeyGroupId > 0) relationOverlay += $"[KEY_{cell.KeyGroupId}] ";
                    if (cell.LockGroupId > 0) relationOverlay += $"[LOCK_{cell.LockGroupId}] ";
                    if (cell.LinkGroupId > 0) relationOverlay += $"[L_{cell.LinkGroupId}] ";

                    if (!string.IsNullOrEmpty(relationOverlay))
                    {
                        Rect tagRect = new Rect(cellRect.x + 2, cellRect.y + 25, cellRect.width - 4, 20f);
                        EditorGUI.DrawRect(tagRect, new Color(0.1f, 0.1f, 0.1f, 0.85f));
                        GUI.Label(tagRect, relationOverlay, EditorStyles.whiteMiniLabel);
                    }

                    if (cell.StartHidden && hasColor)
                    {
                        Rect hiddenIndicatorRect = new Rect(cellRect.x + 2, cellRect.y + cellRect.height - 20, cellRect.width - 4, 20f);
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

                // Interactive Context Configuration Processing
                if (cellRect.Contains(e.mousePosition) && e.type == EventType.ContextClick)
                {
                    GenericMenu menu = new GenericMenu();

                    if (cell.Behavior == CellBehavior.Standard && !string.IsNullOrEmpty(cell.AssignedColorId))
                    {
                        menu.AddItem(new GUIContent("Unit/Start Revealed"), !cell.StartHidden, () => { cell.StartHidden = false; Repaint(); });
                        menu.AddItem(new GUIContent("Unit/Start Hidden"), cell.StartHidden, () => { cell.StartHidden = true; Repaint(); });
                        menu.AddSeparator("Unit/");

                        // Gated Option Verification via Mutual Exclusion
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
                        menu.AddItem(new GUIContent("Convert Cell/To Blocker (Hole)"), false, () => { ResetRelations(cell); cell.Behavior = CellBehavior.Blocker; cell.AssignedColorId = ""; cell.StartHidden = false; Repaint(); });
                        menu.AddItem(new GUIContent("Convert Cell/To Pipe Generator"), false, () => { ResetRelations(cell); cell.Behavior = CellBehavior.Pipe; cell.AssignedColorId = ""; cell.StartHidden = false; Repaint(); });
                    }
                    else
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
                string colorStr = generatedQueues[q][c].ColorId;

                EditorGUI.DrawRect(containerRect, GetColorValue(colorStr));
                GUIStyle labelOverride = new GUIStyle(EditorStyles.whiteBoldLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter };
                EditorGUI.LabelField(containerRect, colorStr.ToUpper(), labelOverride);
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

    private Color GetColorValue(string colorId)
    {
        if (string.IsNullOrEmpty(colorId)) return Color.white;
        string[] parts = colorId.Split('_');
        if (parts.Length < 2 || !int.TryParse(parts[1], out int index)) return Color.gray;

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

    // =========================================================================
    // PERSISTENCE ENGINE: DETERMINISTIC COORDINATE-TO-GUID CONVERSIONS
    // =========================================================================

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

        gridColumns = levelData.Grid.Columns;
        gridRows = levelData.Grid.Rows;
        queueCount = levelData.ResolutionQueues.Count;

        BuildCanvas();
        generatedQueues = levelData.ResolutionQueues;

        // Build temporary reverse matrix mapping from standard coordinates to their Guid values
        Dictionary<Guid, Vector2Int> guidToCoordMap = new Dictionary<Guid, Vector2Int>();
        foreach (var cellNode in levelData.Grid.Matrix)
        {
            if (cellNode.OccupyingUnit != null)
            {
                guidToCoordMap[cellNode.OccupyingUnit.Id] = new Vector2Int(cellNode.Position.X, cellNode.Position.Y);
            }
        }

        int keyGroupCounter = 1;
        int linkGroupCounter = 1;
        Dictionary<int, int> schemaKeyToEditorId = new Dictionary<int, int>();

        foreach (var cellNode in levelData.Grid.Matrix)
        {
            Vector2Int key = new Vector2Int(cellNode.Position.X, cellNode.Position.Y);
            if (!editorMatrix.ContainsKey(key)) continue;

            EditorCell cell = editorMatrix[key];

            if (!cellNode.IsPlayablePath)
            {
                cell.Behavior = CellBehavior.Blocker;
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

                // Restore Complex Feature Groups during deserialization passes
                if (cellNode.OccupyingUnit != null)
                {
                    // 1. Process Linked Groups Reconstruction
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

                    // 2. Process Explicit Dependency Chains Reconstruction
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

        // Generate matching tracking Guids mapping exactly to coordinates beforehand
        Dictionary<Vector2Int, Guid> positionToGuidMap = new Dictionary<Vector2Int, Guid>();
        foreach (var kvp in editorMatrix)
        {
            positionToGuidMap[kvp.Key] = Guid.NewGuid();
        }

        foreach (var kvp in editorMatrix.OrderBy(p => p.Key.y).ThenBy(p => p.Key.x))
        {
            var coord = new GameLevelSchema.Coordinate(kvp.Key.x, kvp.Key.y);
            var node = new GameLevelSchema.CellNode { Position = coord };

            if (kvp.Value.Behavior == CellBehavior.Blocker)
            {
                node.IsPlayablePath = false;
            }
            else if (kvp.Value.Behavior == CellBehavior.Pipe)
            {
                node.ContinuousPipe = new GameLevelSchema.PipeGenerator { MaxTotalEmissions = kvp.Value.PipeEmissions };
                for (int i = 0; i < kvp.Value.PipeEmissions; i++)
                {
                    var unit = new GameLevelSchema.GridUnit { Id = Guid.NewGuid() };
                    string color = !string.IsNullOrEmpty(kvp.Value.AssignedColorId) ? kvp.Value.AssignedColorId : "Color_0";
                    for (int d = 0; d < 9; d++) unit.InteriorContents.Add(new GameLevelSchema.DumplingItem { ColorId = color });
                    node.ContinuousPipe.ReservoirQueue.Add(unit);
                }
            }
            else if (kvp.Value.Behavior == CellBehavior.Standard)
            {
                if (!string.IsNullOrEmpty(kvp.Value.AssignedColorId))
                {
                    node.OccupyingUnit = new GameLevelSchema.GridUnit { Id = positionToGuidMap[kvp.Key] };
                    node.OccupyingUnit.IsHiddenUntilUnblocked = kvp.Value.StartHidden;
                    
                    for (int d = 0; d < 9; d++) 
                    {
                        node.OccupyingUnit.InteriorContents.Add(new GameLevelSchema.DumplingItem { ColorId = kvp.Value.AssignedColorId });
                    }

                    // Map Local Numerical Lock IDs to Schema Target Guids
                    if (kvp.Value.LockGroupId > 0)
                    {
                        foreach (var cellPair in editorMatrix)
                        {
                            if (cellPair.Value.KeyGroupId == kvp.Value.LockGroupId)
                            {
                                node.OccupyingUnit.ExplicitlyBlockedByUnitIds.Add(positionToGuidMap[cellPair.Key]);
                            }
                        }
                    }

                    // Map Local Numerical Link IDs to Schema Partner Guids
                    if (kvp.Value.LinkGroupId > 0)
                    {
                        foreach (var cellPair in editorMatrix)
                        {
                            if (cellPair.Key != kvp.Key && cellPair.Value.LinkGroupId == kvp.Value.LinkGroupId)
                            {
                                node.OccupyingUnit.LinkedUnitIds.Add(positionToGuidMap[cellPair.Key]);
                            }
                        }
                    }
                }
            }
            level.Grid.Matrix.Add(node);
        }
        return level;
    }
}