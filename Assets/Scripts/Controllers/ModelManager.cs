using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;
using System;

public class ModelManager : MonoBehaviour
{
    const string LAST_PLAYED_LEVEL = "LastPlayedLevel";
    const string GOLD_AMOUNT = "GoldAmount";

    public const int GOLD_PER_WIN = 50;
    public const int REVIVE_COST = 50;
    
    public static ModelManager Instance { get; private set; }

    [SerializeField] private List<TextAsset> levelTextAssets = new List<TextAsset>();

    private List<GameLevelSchema> loadedLevels = new List<GameLevelSchema>();

    List<int> unlocksIndexList = new List<int>();
    public List<int> UnlocksIndexList { get => unlocksIndexList; set => unlocksIndexList = value; }
    public int LevelCount => loadedLevels.Count;

    
    int coinsAmount = 0;

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

        unlocksIndexList.Add(15); //bonus bubbles

        coinsAmount = LoadBalance();
    }

    public GameLevelSchema GetLevelByIndex(int index)
    {
        int targetedIndex = index % loadedLevels.Count;
        string json = JsonConvert.SerializeObject(loadedLevels[targetedIndex]);
        return JsonConvert.DeserializeObject<GameLevelSchema>(json);
    }

    internal int GetBalance()
    {
        return coinsAmount;
    }

    public void AddToBalanceAndSave(int amount)
    {
        coinsAmount += amount;

        PlayerPrefs.SetInt(GOLD_AMOUNT, coinsAmount);
    }

    private int LoadBalance()
    {
        return PlayerPrefs.GetInt(GOLD_AMOUNT, 0);
    }

    public int GetLastPlayedLevel()
    {
        return PlayerPrefs.GetInt(LAST_PLAYED_LEVEL, -1);
    }

    public void SetLastPlayedLevel(int level)
    {
        PlayerPrefs.SetInt(LAST_PLAYED_LEVEL, level);
    }
}