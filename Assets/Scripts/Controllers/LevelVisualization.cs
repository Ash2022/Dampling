using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class LevelVisualization : MonoBehaviour
{
    [Header("Data Configurations")]
    public TextAsset LevelJsonFile;

    [Header("Visual Prefabs")]
    public GameObject UnitPrefab;
    public GameObject ContainerPrefab;

    [Header("Manual Y-Axis Baselines")]
    [Tooltip("The vertical position of the first container (head of the queue). Subsequent containers stack upwards (+Y).")]
    public float QueueBottomY = 3.0f;
    [Tooltip("The vertical position of the top grid row (Y=0). Subsequent rows stack downwards (-Y).")]
    public float GridTopY = -1.0f;

    // --- Internal Runtime Object Tracking ---
    private List<GameObject> spawnedVisualElements = new List<GameObject>();

    void Start()
    {
        BuildLevel();
    }

    /// <summary>
    /// Parses the configured JSON file data and instantiates the 2D visual layout in the scene.
    /// </summary>
    
    public void BuildLevel()
    {
        ClearCurrentVisualization();

        if (LevelJsonFile == null)
        {
            Debug.LogError("Cannot build level visualization! Level JSON TextAsset is missing.");
            return;
        }

        // 1. Read the raw text file safely using standard JSON conversion
        GameLevelSchema rawData = Newtonsoft.Json.JsonConvert.DeserializeObject<GameLevelSchema>(LevelJsonFile.text);
        if (rawData == null)
        {
            Debug.LogError("Failed to deserialize GameLevelSchema from text asset.");
            return;
        }

        // 2. NEW: Pass it straight through our custom utility class to reconstitute the missing matrix dictionary perfectly!
        GameLevelSchema levelData = DamplingGameUtils.CloneLevelSchema(rawData);

        Vector2 unitSize = GetPrefabSize(UnitPrefab);
        Vector2 containerSize = GetPrefabSize(ContainerPrefab);

        // 3. GENERATE AND CENTER DEMAND QUEUES
        int totalQueues = levelData.ResolutionQueues.Count;
        float totalQueuesWidth = totalQueues * containerSize.x;
        float queueStartX = -(totalQueuesWidth / 2f) + (containerSize.x / 2f);

        for (int q = 0; q < totalQueues; q++)
        {
            float targetX = queueStartX + (q * containerSize.x);
            var activeQueueList = levelData.ResolutionQueues[q];

            for (int c = 0; c < activeQueueList.Count; c++)
            {
                float targetY = QueueBottomY + (c * containerSize.y);
                Vector3 spawnPosition = new Vector3(targetX, targetY, 0f);

                GameObject containerInstance = Instantiate(ContainerPrefab, spawnPosition, Quaternion.identity, transform);
                spawnedVisualElements.Add(containerInstance);

                containerInstance.name = $"Container_Q{q}_Idx{c}_{activeQueueList[c].ColorId}";
                ApplyColorTint(containerInstance, activeQueueList[c].ColorId);
            }
        }

        // 4. GENERATE AND CENTER SUPPLY GRID MAP
        int columns = levelData.Grid.Columns;
        float totalGridWidth = columns * unitSize.x;
        float gridStartX = -(totalGridWidth / 2f) + (unitSize.x / 2f);

        // This loop will now execute perfectly because levelData.Grid.Matrix is fully populated!
        foreach (var kvp in levelData.Grid.Matrix)
        {
            int gridX = kvp.Key.X;
            int gridY = kvp.Key.Y;
            var cellNode = kvp.Value;

            if (!cellNode.IsPlayablePath) continue;

            float worldX = gridStartX + (gridX * unitSize.x);
            float worldY = GridTopY - (gridY * unitSize.y);
            Vector3 spawnPosition = new Vector3(worldX, worldY, 0f);

            GameObject unitInstance = Instantiate(UnitPrefab, spawnPosition, Quaternion.identity, transform);
            spawnedVisualElements.Add(unitInstance);

            if (cellNode.ContinuousPipe != null)
            {
                unitInstance.name = $"PipeUnit_({gridX},{gridY})";
                var firstUnit = cellNode.ContinuousPipe.ReservoirQueue.FirstOrDefault();
                string pipeColorId = firstUnit?.InteriorContents.FirstOrDefault()?.ColorId ?? "";
                ApplyColorTint(unitInstance, pipeColorId);
            }
            else if (cellNode.OccupyingUnit != null)
            {
                unitInstance.name = $"StandardUnit_({gridX},{gridY})";
                string unitColorId = cellNode.OccupyingUnit.InteriorContents.FirstOrDefault()?.ColorId ?? "";
                ApplyColorTint(unitInstance, unitColorId);
                
                if (cellNode.OccupyingUnit.IsHiddenUntilUnblocked)
                {
                    ApplyHiddenStateOverlay(unitInstance);
                }
            }
            else
            {
                unitInstance.name = $"EmptyCell_({gridX},{gridY})";
                ApplyColorTint(unitInstance, "");
            }
        }
    }

    /// <summary>
    /// Destroys all currently spawned objects to clear the workspace canvas before rebuilding.
    /// </summary>
    public void ClearCurrentVisualization()
    {
        foreach (var element in spawnedVisualElements)
        {
            if (element != null)
            {
                if (Application.isPlaying) Destroy(element);
                else DestroyImmediate(element);
            }
        }
        spawnedVisualElements.Clear();
    }

    // --- Helper Utility Methods ---

    private Vector2 GetPrefabSize(GameObject prefab)
    {
        if (prefab == null) return Vector2.one;
        var spriteRenderer = prefab.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            return spriteRenderer.bounds.size;
        }
        return Vector2.one; // Fallback bounds size if sprite cannot be parsed safely
    }

    private void ApplyColorTint(GameObject targetObject, string colorId)
    {
        var spriteRenderer = targetObject.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) return;

        if (string.IsNullOrEmpty(colorId))
        {
            spriteRenderer.color = new Color(0.85f, 0.85f, 0.85f, 1f); // Empty fallback gray
            return;
        }

        string[] parts = colorId.Split('_');
        if (parts.Length < 2 || !int.TryParse(parts[1], out int index))
        {
            spriteRenderer.color = Color.gray;
            return;
        }

        // Match color array metrics straight out of your editor script profile setup
        spriteRenderer.color = index switch
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

    private void ApplyHiddenStateOverlay(GameObject targetObject)
    {
        var spriteRenderer = targetObject.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) return;

        // Multiply down current sprite alpha/colors to create a distinct dimmed visual profile
        Color baseColor = spriteRenderer.color;
        spriteRenderer.color = new Color(baseColor.r * 0.3f, baseColor.g * 0.3f, baseColor.b * 0.3f, 1.0f);
    }
}