using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

public class LevelColorFilterWindow : EditorWindow
{
    private string sourceFolder = "Assets/Resources/ThematicLevels";
    private int minColors = 6;

    [MenuItem("Tools/Level Color Filter")]
    public static void ShowWindow() => GetWindow<LevelColorFilterWindow>("Color Filter");

    private void OnGUI()
    {
        GUILayout.BeginHorizontal();
        sourceFolder = EditorGUILayout.TextField("Source Folder", sourceFolder);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string path = EditorUtility.OpenFolderPanel("Select Folder", "Assets", "");
            if (path != "") sourceFolder = path;
        }
        GUILayout.EndHorizontal();

        minColors = EditorGUILayout.IntSlider("Minimum Colors", minColors, 4, 8);

        if (GUILayout.Button("Filter Levels"))
        {
            ProcessFiles();
        }
    }

    private void ProcessFiles()
    {
        string excludeFolder = Path.Combine(sourceFolder, "Excluded");
        Directory.CreateDirectory(excludeFolder);

        string[] files = Directory.GetFiles(sourceFolder, "*.json");

        foreach (string file in files)
        {
            string json = File.ReadAllText(file);
            GameLevelSchema level = JsonConvert.DeserializeObject<GameLevelSchema>(json);

            HashSet<int> colors = new HashSet<int>();
            foreach (var queue in level.ResolutionQueues)
            {
                foreach (var container in queue)
                {
                    colors.Add(container.ColorIndex);
                }
            }

            if (colors.Count < minColors)
            {
                string dest = Path.Combine(excludeFolder, Path.GetFileName(file));
                File.Move(file, dest);
                File.Delete(file + ".meta");
            }
        }

        AssetDatabase.Refresh();
    }
}