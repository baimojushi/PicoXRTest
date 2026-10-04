using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class RunDiagnostics
{
    [MenuItem("Tools/Gallery/Run Diagnostics With Scene Load")]
    public static void Run()
    {
        // 1. Load the scene
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/GalleryScene.unity");
        Debug.Log($"Scene loaded: {scene.name}, isLoaded={scene.isLoaded}");

        // 2. Run diagnostics
        DeepCollisionDiagnostics.Diagnose();
    }
}