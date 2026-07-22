using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;
using System;

public class ModelManager : MonoBehaviour
{
    public const int MAGNET_UNLOCKED = 1;
    public const int SHUFFLE_UNLOCKED = 2;

    private const string PLAYER_DATA_KEY = "PlayerDataSaveState";
    public const int GOLD_PER_WIN = 50;
    public const int REVIVE_COST = 50;

    public static ModelManager Instance { get; private set; }

    [SerializeField] private List<TextAsset> levelTextAssets = new List<TextAsset>();
    private List<GameLevelSchema> loadedLevels = new List<GameLevelSchema>();

    public List<int> UnlocksIndexList { get; set; } = new List<int>();
    public int LevelCount => loadedLevels.Count;

    public PlayerData Data { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    public void Initialize()
    {
        loadedLevels.Clear();
        foreach (var asset in levelTextAssets)
        {
            GameLevelSchema levelData = JsonConvert.DeserializeObject<GameLevelSchema>(asset.text);
            loadedLevels.Add(levelData);
        }

        UnlocksIndexList.Add(15);

        LoadData();
    }

    public GameLevelSchema GetLevelByIndex(int index)
    {
        int targetedIndex = index % loadedLevels.Count;
        string json = JsonConvert.SerializeObject(loadedLevels[targetedIndex]);
        return JsonConvert.DeserializeObject<GameLevelSchema>(json);
    }

    public int GetBalance() => Data.CoinsAmount;

    public void AddToBalanceAndSave(int amount)
    {
        Data.CoinsAmount += amount;
        SaveData();
    }

    internal int GetUnlock(int currLevelIndex)
    {
        int index = -1;

        if (UnlocksIndexList.Contains(currLevelIndex))
            index = UnlocksIndexList.FindIndex(x => x.Equals(currLevelIndex));

        return index;
    }

    public int GetLastPlayedLevel() => Data.LastPlayedLevel;

    public void SetLastPlayedLevel(int level)
    {
        Data.LastPlayedLevel = level;
        SaveData();
    }

    public void SaveData()
    {
        string json = JsonConvert.SerializeObject(Data);
        PlayerPrefs.SetString(PLAYER_DATA_KEY, json);
        PlayerPrefs.Save();
    }

    private void LoadData()
    {
        if (PlayerPrefs.HasKey(PLAYER_DATA_KEY))
        {
            string json = PlayerPrefs.GetString(PLAYER_DATA_KEY);
            Data = JsonConvert.DeserializeObject<PlayerData>(json);
        }
        else
        {
            Data = new PlayerData();
        }
    }

    public void AdjustMagnetCount(int amount)
    {
        Data.MagnetBoosterCount = Mathf.Max(0, Data.MagnetBoosterCount + amount);
        SaveData();
    }

    public void AdjustShuffleCount(int amount)
    {
        Data.ShuffleBoosterCount = Mathf.Max(0, Data.ShuffleBoosterCount + amount);
        SaveData();
    }

    [Serializable]
    public class PlayerData
    {
        public int LastPlayedLevel = -1;
        public int CoinsAmount = 0;
        public int MagnetBoosterCount = 3;
        public int ShuffleBoosterCount = 3;
    }
}

