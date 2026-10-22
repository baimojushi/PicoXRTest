using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class PlayModeTest
{
    [MenuItem("Tools/Gallery/Play Mode Full Test")]
    public static void Run()
    {
        Debug.Log("=== PLAY MODE FULL TEST ===");
        
        // 1. Load the scene
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/GalleryScene.unity");
        Debug.Log($"Scene loaded: {scene.name}, isLoaded={scene.isLoaded}");
        
        // 2. Run diagnostics
        DeepCollisionDiagnostics.Diagnose();
        
        // 3. Check SplatZoneManager
        var zoneManager = GameObject.FindObjectOfType<GalleryRuntime.SplatZoneManager>();
        if (zoneManager == null)
        {
            Debug.LogError("SplatZoneManager not found!");
            return;
        }
        Debug.Log($"SplatZoneManager found: observer={zoneManager.observer?.name}, visualRoot={zoneManager.visualRoot?.name}");
        
        // 4. Check Camera tag
        var camera = GameObject.Find("Camera");
        if (camera == null)
        {
            Debug.LogError("Camera not found!");
            return;
        }
        Debug.Log($"Camera tag: '{camera.tag}' (should be MainCamera)");
        
        // 5. Check Player
        var player = GameObject.Find("Player");
        if (player == null)
        {
            Debug.LogError("Player not found!");
            return;
        }
        var cc = player.GetComponent<CharacterController>();
        Debug.Log($"Player pos={player.transform.position}, CC enabled={cc.enabled}, radius={cc.radius}, height={cc.height}");
        
        // 6. Simulate Awake on GalleryPlayerController
        var controller = player.GetComponent<GalleryRuntime.GalleryPlayerController>();
        if (controller != null)
        {
            controller.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
            Debug.Log($"GalleryPlayerController Awake called");
        }
        
        // 7. Verify Camera tag after Awake
        Debug.Log($"Camera tag after Awake: '{camera.tag}'");
        
        // 8. Check GSPLAT_ENABLE_URP define
        var defines = UnityEditor.PlayerSettings.GetScriptingDefineSymbolsForGroup(UnityEditor.BuildTargetGroup.Standalone);
        Debug.Log($"Scripting defines: {defines}");
        
        Debug.Log("=== TEST COMPLETE ===");
    }
}