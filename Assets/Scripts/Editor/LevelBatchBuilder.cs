using System.IO;
using UnityEngine;
using Newtonsoft.Json;

public class LevelBatchBuilder
{
    private DamplingSimulationAgent botAgent = new DamplingSimulationAgent();

    public void GenerateBatch(int startLevel, int endLevel, string outputFolderPath)
    {
        if (!Directory.Exists(outputFolderPath))
            Directory.CreateDirectory(outputFolderPath);

        for (int currentLevel = startLevel; currentLevel <= endLevel; currentLevel++)
        {
            var rules = LevelGeneratorConfig.GetRulesForLevel(currentLevel);
            GameLevelSchema validLevel = null;
            int attemptCount = 0;

            // --- THE CALIBRATION LOOP ---
            while (validLevel == null && attemptCount < 50) 
            {
                attemptCount++;
                
                // 1. Generate a candidate based on the rules
                GameLevelSchema candidate = GenerateCandidateLevel(rules, currentLevel);
                
                // 2. Test it with the bot (e.g., 200 runs to get a solid win rate)
                var report = botAgent.RunBatchSimulation(candidate, 200);

                // 3. Evaluate against our target
                float minAcceptable = rules.TargetWinRate - rules.WinRateTolerance;
                float maxAcceptable = rules.TargetWinRate + rules.WinRateTolerance;

                // Divide by 100 because your bot returns 0-100, but our target is 0.0-1.0
                float actualWinRate = report.WinRatePercentage / 100f; 

                if (actualWinRate >= minAcceptable && actualWinRate <= maxAcceptable)
                {
                    validLevel = candidate; // We found a winner!
                    Debug.Log($"Level {currentLevel} baked in {attemptCount} attempts. Target: {rules.TargetWinRate}, Actual: {actualWinRate}");
                }
            }

            if (validLevel != null)
            {
                SaveLevelToJson(validLevel, currentLevel, outputFolderPath);
            }
            else
            {
                Debug.LogError($"Failed to generate Level {currentLevel} within difficulty bounds after 50 attempts.");
            }
        }
    }

    private GameLevelSchema GenerateCandidateLevel(LevelGeneratorConfig.LevelRuleset rules, int levelIndex)
    {
        // THIS IS THE FINAL PIECE WE NEED TO WRITE.
        // It needs to:
        // 1. Pick a grid size between rules.MinGridSize and rules.MaxGridSize
        // 2. Populate standard units using up to rules.MaxColors
        // 3. Sprinkle in features from rules.AllowedMechanics
        // 4. Handle the multi-color pipe lists and unique IDs
        
        return new GameLevelSchema(); // Placeholder
    }

    private void SaveLevelToJson(GameLevelSchema level, int index, string path)
    {
        string fileName = $"Level_{index:000}.json";
        string fullPath = Path.Combine(path, fileName);
        string json = JsonConvert.SerializeObject(level, Formatting.Indented);
        File.WriteAllText(fullPath, json);
    }
}