using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class SetupBuildScenes
{
    [MenuItem("Tools/Gallery/Setup Build Scenes")]
    public static void Setup()
    {
        var scenes = new EditorBuildSettingsScene[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/GalleryScene.unity", true)
        };
        EditorBuildSettings.scenes = scenes;
        Debug.Log("Build scenes configured: GalleryScene");
    }
}