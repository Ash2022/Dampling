using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;
using System;

public class ModelManager : MonoBehaviour
{
    const int HIDDEN_UNIT_UNLOCKED = 4;
    const int MAGNET_UNLOCKED = 6;
    const int SHUFFLE_UNLOCKED = 8;
    const int PIPE_UNIT_UNLOCKED = 10;
    const int ICE_UNIT_UNLOCKED = 15;
    const int LOCK_KEY_UNLOCKED = 25;
    const int LINK_UNLOCKED = 40;
    const int HIDDEN_CONTAINER = 55;
    //const int COVER_UNLOCKED = 75;

    
    

    public const int MAGNET_BOOSTER = 1;
    public const int SHUFFLE_BOOSTER = 2;

    public const int LOOP_SIZE = 20;

    private const string PLAYER_DATA_KEY = "PlayerDataSaveState";
    public const int GOLD_PER_WIN = 50;
    private const int REVIVE_COST = 50;

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

        UnlocksIndexList.Add(HIDDEN_UNIT_UNLOCKED);
        UnlocksIndexList.Add(MAGNET_UNLOCKED);
        UnlocksIndexList.Add(SHUFFLE_UNLOCKED);
        UnlocksIndexList.Add(PIPE_UNIT_UNLOCKED);
        UnlocksIndexList.Add(ICE_UNIT_UNLOCKED);
        UnlocksIndexList.Add(LOCK_KEY_UNLOCKED);
        UnlocksIndexList.Add(LINK_UNLOCKED);
        UnlocksIndexList.Add(HIDDEN_CONTAINER);



        LoadData();
    }

    public GameLevelSchema GetLevelByIndex(int index)
    {
        int numLevels = loadedLevels.Count;
        int loopedIndex = index;

        while (loopedIndex >= numLevels)
            loopedIndex -= LOOP_SIZE;

        string json = JsonConvert.SerializeObject(loadedLevels[loopedIndex]);
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

    public void DeleteData()
    {
        int currentMoney = Data.CoinsAmount;
        PlayerPrefs.DeleteKey(PLAYER_DATA_KEY);
        PlayerPrefs.Save();
        Data = new PlayerData();
        Data.CoinsAmount = currentMoney;
        SaveData();
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

    public int GetReviveCost()
    {
        return REVIVE_COST* Data.revivesUsed;
    }

    public void UseRevive()
    {
        Data.revivesUsed++;
        SaveData();
    }

    [Serializable]
    public class PlayerData
    {
        public int LastPlayedLevel = -1;
        public int CoinsAmount = 0;
        public int MagnetBoosterCount = 3;
        public int ShuffleBoosterCount = 3;
        public int revivesUsed =1;
    }
}

