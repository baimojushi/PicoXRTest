using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Phase 4 Scene Inspector - Print scene contents
/// </summary>
public static class Phase4SceneInspector
{
    [MenuItem("Phase 4/Inspect Scene")]
    public static void InspectScene()
    {
        var scene = SceneManager.GetActiveScene();
        Debug.Log($"=== Scene: {scene.name} ===");
        Debug.Log($"Scene path: {scene.path}");
        Debug.Log($"Is loaded: {scene.isLoaded}");
        Debug.Log($"Root game objects: {scene.rootCount}");

        var roots = new List<GameObject>();
        scene.GetRootGameObjects(roots);

        foreach (var go in roots)
        {
            PrintHierarchy(go, 0);
        }
    }

    static void PrintHierarchy(GameObject go, int depth)
    {
        string indent = new string(' ', depth * 2);
        var components = go.GetComponents<Component>();
        string compNames = string.Join(", ", System.Array.ConvertAll(components, c => c?.GetType().Name ?? "null"));
        Debug.Log($"{indent}{go.name} (active={go.activeInHierarchy}) [{compNames}]");

        foreach (Transform child in go.transform)
        {
            PrintHierarchy(child.gameObject, depth + 1);
        }
    }

    [MenuItem("Phase 4/Check Camera & Player")]
    public static void CheckCameraAndPlayer()
    {
        Debug.Log("=== Camera & Player Check ===");
        
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            Debug.Log($"Camera.main: FOUND - {mainCam.name}, enabled={mainCam.enabled}, tag={mainCam.tag}");
            Debug.Log($"  Position: {mainCam.transform.position}");
            Debug.Log($"  Rotation: {mainCam.transform.eulerAngles}");
            Debug.Log($"  ClearFlags: {mainCam.clearFlags}");
            Debug.Log($"  CullingMask: {mainCam.cullingMask}");
        }
        else
        {
            Debug.Log("Camera.main: MISSING");
        }

        // Check URP
        var graphicsSettingsType = typeof(UnityEngine.Rendering.GraphicsSettings);
        var currentRenderPipelineProp = graphicsSettingsType.GetProperty("currentRenderPipeline", 
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        var urpAsset = currentRenderPipelineProp?.GetValue(null);
        if (urpAsset != null)
        {
            Debug.Log($"URP Asset: {((UnityEngine.Object)urpAsset).name} (type: {urpAsset.GetType().Name})");
        }
        else
        {
            Debug.Log("URP Asset: NOT SET");
        }

        // Check Player
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            Debug.Log($"Player: FOUND - {player.name}");
            Debug.Log($"  Position: {player.transform.position}");
            var gpController = player.GetComponent("GalleryPlayerController");
            Debug.Log($"  GalleryPlayerController: {(gpController != null ? "FOUND" : "MISSING")}");
        }
        else
        {
            Debug.Log("Player: NOT FOUND (tag 'Player')");
        }

        // Check SplatZoneManager
        var zoneManagerType = System.Type.GetType("SplatZoneManager, Assembly-CSharp");
        if (zoneManagerType == null)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                zoneManagerType = asm.GetType("SplatZoneManager");
                if (zoneManagerType != null) break;
            }
        }

        if (zoneManagerType != null)
        {
            var findMethod = typeof(UnityEngine.Object).GetMethod("FindFirstObjectByType", 
                new[] { typeof(UnityEngine.FindObjectsInactive), typeof(UnityEngine.FindObjectsSortMode) });
            if (findMethod != null)
            {
                var findGeneric = findMethod.MakeGenericMethod(zoneManagerType);
                var zoneManager = findGeneric.Invoke(null, new object[] { 
                    UnityEngine.FindObjectsInactive.Include, 
                    UnityEngine.FindObjectsSortMode.None 
                });

                if (zoneManager != null)
                {
                    var observerField = zoneManagerType.GetField("observer", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var playerTransformField = zoneManagerType.GetField("_playerTransform", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    Transform observer = null;
                    if (observerField != null) observer = observerField.GetValue(zoneManager) as Transform;
                    else if (playerTransformField != null) observer = playerTransformField.GetValue(zoneManager) as Transform;

                    Debug.Log($"SplatZoneManager: FOUND - observer set = {observer != null}");
                    if (observer != null && player != null)
                    {
                        Debug.Log($"  Observer == Player.transform: {observer == player.transform}");
                    }
                }
                else
                {
                    Debug.Log("SplatZoneManager: NOT IN SCENE");
                }
            }
        }
        else
        {
            Debug.Log("SplatZoneManager: TYPE NOT FOUND");
        }

        // Check GsplatRenderer
        var gsplatRendererType = System.Type.GetType("GsplatRenderer, Assembly-CSharp");
        if (gsplatRendererType == null)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                gsplatRendererType = asm.GetType("GsplatRenderer");
                if (gsplatRendererType != null) break;
            }
        }

        if (gsplatRendererType != null)
        {
            var findMethod = typeof(UnityEngine.Object).GetMethod("FindObjectsByType", 
                new[] { typeof(UnityEngine.FindObjectsInactive), typeof(UnityEngine.FindObjectsSortMode) });
            if (findMethod != null)
            {
                var findGeneric = findMethod.MakeGenericMethod(gsplatRendererType);
                var renderers = findGeneric.Invoke(null, new object[] { 
                    UnityEngine.FindObjectsInactive.Include, 
                    UnityEngine.FindObjectsSortMode.None 
                }) as UnityEngine.Object[];

                int count = renderers?.Length ?? 0;
                int enabledCount = 0;
                if (renderers != null)
                {
                    foreach (var r in renderers)
                    {
                        var enabledProp = gsplatRendererType.GetProperty("enabled");
                        var gameObjectProp = gsplatRendererType.GetProperty("gameObject");
                        bool enabled = (bool)(enabledProp?.GetValue(r) ?? false);
                        var go = gameObjectProp?.GetValue(r) as GameObject;
                        bool activeInHierarchy = go != null && go.activeInHierarchy;
                        if (enabled && activeInHierarchy) enabledCount++;
                    }
                }
                Debug.Log($"GsplatRenderer: total={count}, enabled={enabledCount}");
            }
        }
        else
        {
            Debug.Log("GsplatRenderer: TYPE NOT FOUND");
        }
    }
}