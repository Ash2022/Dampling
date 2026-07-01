using UnityEngine;
using System.Collections.Generic;

public class ModelManager : MonoBehaviour
{
    public static ModelManager Instance { get; private set; }

    [SerializeField] private List<TextAsset> levelTextAssets = new List<TextAsset>();
    
    private List<GameLevelSchema> loadedLevels = new List<GameLevelSchema>();

    private void Awake()
    {
        Instance = this;
    }

    public void Initialize()
    {
        loadedLevels.Clear();
        
        foreach (var asset in levelTextAssets)
        {
            // Use Newtonsoft Json.NET to accurately parse complex, nested multi-dimensional data schemas
            GameLevelSchema levelData = Newtonsoft.Json.JsonConvert.DeserializeObject<GameLevelSchema>(asset.text);
            loadedLevels.Add(levelData);
        }
    }

    public GameLevelSchema GetLevelByIndex(int index)
    {
        int targetedIndex = index % loadedLevels.Count;
        return loadedLevels[targetedIndex];
    }
}