using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;

public class PlayModeSplatTest
{
    [MenuItem("Tools/Gallery/Play Mode Splat Test")]
    public static void Run()
    {
        Debug.Log("=== PLAY MODE SPLAT TEST ===");
        
        // 1. Load the scene
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/GalleryScene.unity");
        Debug.Log($"Scene loaded: {scene.name}, isLoaded={scene.isLoaded}");
        
        // 2. Check SplatZoneManager
        var zoneManager = GameObject.FindObjectOfType<GalleryRuntime.SplatZoneManager>();
        if (zoneManager == null)
        {
            Debug.LogError("SplatZoneManager not found!");
            return;
        }
        Debug.Log($"SplatZoneManager found: observer={zoneManager.observer?.name}, visualRoot={zoneManager.visualRoot?.name}");
        
        // 3. Check manifest file exists
        var manifestPath = Path.Combine(Application.streamingAssetsPath, "Gallery/Config/zones.json");
        Debug.Log($"Manifest path: {manifestPath}, exists={File.Exists(manifestPath)}");
        
        // 4. Check global base PLY exists
        var globalPlyPath = Path.Combine(Application.streamingAssetsPath, "Gallery/Splats/global/gallery_base_low.ply");
        Debug.Log($"Global PLY path: {globalPlyPath}, exists={File.Exists(globalPlyPath)}, size={new FileInfo(globalPlyPath).Length}");
        
        // 5. Simulate SplatZoneManager Start (which loads splats)
        zoneManager.SendMessage("Start", SendMessageOptions.DontRequireReceiver);
        Debug.Log($"SplatZoneManager Start called");
        
        // 6. Check visual root children for GsplatRenderer
        var visualRoot = zoneManager.visualRoot;
        if (visualRoot != null)
        {
            var renderers = visualRoot.GetComponentsInChildren<Gsplat.GsplatRenderer>(true);
            Debug.Log($"GsplatRenderers in visualRoot: {renderers.Length}");
            foreach (var r in renderers)
            {
                Debug.Log($"  Renderer: {r.gameObject.name}, enabled={r.enabled}, asset={r.GsplatAsset?.name ?? "null"}");
            }
        }
        
        Debug.Log("=== SPLAT TEST COMPLETE ===");
    }
}