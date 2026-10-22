using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public class RunDiagnosticsWithScene
{
    [MenuItem("Tools/Gallery/Run Diagnostics With Scene")]
    public static void Run()
    {
        // 1. Load the scene
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/GalleryScene.unity");
        Debug.Log($"Scene loaded: {scene.name}, isLoaded={scene.isLoaded}");

        // 2. Run diagnostics
        DeepCollisionDiagnostics.Diagnose();
    }
}