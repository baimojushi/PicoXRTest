using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Reflection;
using System;

/// <summary>
/// Phase 4 Camera Audit - Editor script using reflection to avoid assembly dependencies
/// </summary>
public static class Phase4CameraAuditRunner
{
    [MenuItem("Phase 4/Run Camera Audit")]
    public static void RunAudit()
    {
        var results = new List<string>();
        results.Add("=== Phase 4 Camera & Rendering Audit ===");

        // 1. Camera.main
        Camera mainCam = Camera.main;
        bool hasMainCamera = mainCam != null;
        bool mainCameraEnabled = false;
        bool mainCameraTagCorrect = false;

        results.Add($"Camera.main: {(hasMainCamera ? "FOUND" : "MISSING")}");

        if (hasMainCamera)
        {
            mainCameraEnabled = mainCam.enabled;
            results.Add($"  Enabled: {mainCameraEnabled}");

            mainCameraTagCorrect = mainCam.CompareTag("MainCamera");
            results.Add($"  Tag == 'MainCamera': {mainCameraTagCorrect}");

            // Camera.renderType doesn't exist in this Unity version - check cameraType instead
            results.Add($"  Camera Type: {mainCam.cameraType}");

            results.Add($"  Name: {mainCam.name}");
            results.Add($"  Position: {mainCam.transform.position}");
            results.Add($"  ClearFlags: {mainCam.clearFlags}");
            results.Add($"  cullingMask: {mainCam.cullingMask}");
        }

        // 2. GraphicsSettings.currentRenderPipeline (using reflection)
        var graphicsSettingsType = typeof(GraphicsSettings);
        var currentRenderPipelineProp = graphicsSettingsType.GetProperty("currentRenderPipeline", BindingFlags.Static | BindingFlags.Public);
        var urpAsset = currentRenderPipelineProp?.GetValue(null);

        bool urpRendererDataValid = false;
        bool hasGsplatURPFeature = false;

        if (urpAsset != null)
        {
            string urpTypeName = urpAsset.GetType().Name;
            results.Add($"\nRender Pipeline: {((UnityEngine.Object)urpAsset).name} (type: {urpTypeName})");

            if (urpTypeName.Contains("Universal"))
            {
                urpRendererDataValid = true;

                // Use reflection to access scriptableRendererData
                var scriptableRendererDataProp = urpAsset.GetType().GetProperty("scriptableRendererData");
                if (scriptableRendererDataProp != null)
                {
                    var rendererDataList = scriptableRendererDataProp.GetValue(urpAsset) as UnityEngine.Object[];
                    if (rendererDataList != null && rendererDataList.Length > 0)
                    {
                        foreach (var rendererData in rendererDataList)
                        {
                            if (rendererData == null) continue;

                            var rendererFeaturesProp = rendererData.GetType().GetProperty("rendererFeatures");
                            if (rendererFeaturesProp != null)
                            {
                                var features = rendererFeaturesProp.GetValue(rendererData) as UnityEngine.Object[];
                                bool hasFeature = false;
                                if (features != null)
                                {
                                    foreach (var feature in features)
                                    {
                                        if (feature != null && ((UnityEngine.Object)feature).name.Contains("Gsplat", StringComparison.OrdinalIgnoreCase))
                                        {
                                            hasFeature = true;
                                            hasGsplatURPFeature = true;
                                            results.Add($"  ✓ Renderer '{((UnityEngine.Object)rendererData).name}' has Gsplat URP Feature: {((UnityEngine.Object)feature).name}");
                                            break;
                                        }
                                    }
                                }
                                if (!hasFeature)
                                {
                                    results.Add($"  ✗ Renderer '{((UnityEngine.Object)rendererData).name}' MISSING Gsplat URP Feature");
                                }
                            }
                        }
                    }
                    else
                    {
                        results.Add("  No ScriptableRendererData assigned!");
                    }
                }
            }
            else
            {
                results.Add($"  ⚠ Not Universal RP (type: {urpTypeName})");
            }
        }
        else
        {
            results.Add("\n✗ No Render Pipeline assigned in GraphicsSettings!");
        }

        // 3. Check GsplatRenderer using FindObjectsOfTypeAll (works for non-MonoBehaviour too)
        var gsplatRendererType = System.Type.GetType("GsplatRenderer, Assembly-CSharp");
        if (gsplatRendererType == null)
        {
            // Try other assemblies
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                gsplatRendererType = asm.GetType("GsplatRenderer");
                if (gsplatRendererType != null) break;
            }
        }

        if (gsplatRendererType != null)
        {
            var findMethod = typeof(UnityEngine.Object).GetMethod("FindObjectsByType", new[] { typeof(UnityEngine.FindObjectsInactive), typeof(UnityEngine.FindObjectsSortMode) });
            if (findMethod != null)
            {
                var findGeneric = findMethod.MakeGenericMethod(gsplatRendererType);
                var gsplatRenderers = findGeneric.Invoke(null, new object[] { UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None }) as UnityEngine.Object[];

                int gsplatRendererCount = gsplatRenderers?.Length ?? 0;
                int enabledGsplatRendererCount = 0;

                if (gsplatRenderers != null)
                {
                    foreach (var r in gsplatRenderers)
                    {
                        var enabledProp = gsplatRendererType.GetProperty("enabled");
                        var gameObjectProp = gsplatRendererType.GetProperty("gameObject");
                        var assetProp = gsplatRendererType.GetProperty("asset");

                        bool enabled = (bool)(enabledProp?.GetValue(r) ?? false);
                        var go = gameObjectProp?.GetValue(r) as GameObject;
                        bool activeInHierarchy = go != null && go.activeInHierarchy;

                        if (enabled && activeInHierarchy) enabledGsplatRendererCount++;

                        string assetName = assetProp?.GetValue(r) as UnityEngine.Object != null ? (assetProp.GetValue(r) as UnityEngine.Object).name : "null";
                        results.Add($"  {((UnityEngine.Object)r).name}: enabled={enabled}, active={activeInHierarchy}, asset={assetName}");
                    }
                }

                results.Add($"\nGsplatRenderer total: {gsplatRendererCount}, enabled: {enabledGsplatRendererCount}");
            }
        }
        else
        {
            results.Add("\n⚠ GsplatRenderer type not found in loaded assemblies");
        }

        // 4. SplatZoneManager Observer (reflection)
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
            var findMethod = typeof(UnityEngine.Object).GetMethod("FindFirstObjectByType", new[] { typeof(UnityEngine.FindObjectsInactive), typeof(UnityEngine.FindObjectsSortMode) });
            if (findMethod != null)
            {
                var findGeneric = findMethod.MakeGenericMethod(zoneManagerType);
                var zoneManager = findGeneric.Invoke(null, new object[] { UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None });

                if (zoneManager != null)
                {
                    var observerField = zoneManagerType.GetField("observer", BindingFlags.NonPublic | BindingFlags.Instance);
                    var playerTransformField = zoneManagerType.GetField("_playerTransform", BindingFlags.NonPublic | BindingFlags.Instance);

                    Transform observer = null;
                    if (observerField != null) observer = observerField.GetValue(zoneManager) as Transform;
                    else if (playerTransformField != null) observer = playerTransformField.GetValue(zoneManager) as Transform;

                    bool splatZoneManagerHasObserver = observer != null;
                    results.Add($"\nSplatZoneManager: observer set = {splatZoneManagerHasObserver}");

                    if (splatZoneManagerHasObserver)
                    {
                        var playerObj = GameObject.FindGameObjectWithTag("Player");
                        bool observerIsPlayerTransform = (playerObj != null && observer == playerObj.transform);
                        results.Add($"  Observer is Player.transform: {observerIsPlayerTransform}");
                        if (!observerIsPlayerTransform)
                        {
                            results.Add($"  Observer: {observer.name} (path: {GetGameObjectPath(observer.gameObject)})");
                        }
                    }
                }
                else
                {
                    results.Add("\n✗ SplatZoneManager not found in scene");
                }
            }
        }
        else
        {
            results.Add("\n⚠ SplatZoneManager type not found");
        }

        // 5. Player & GalleryPlayerController
        var player = GameObject.FindGameObjectWithTag("Player");
        results.Add($"\nPlayer (tag): {(player != null ? "FOUND" : "MISSING")}");
        if (player != null)
        {
            var galleryPlayerControllerType = System.Type.GetType("GalleryPlayerController, Assembly-CSharp");
            if (galleryPlayerControllerType == null)
            {
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    galleryPlayerControllerType = asm.GetType("GalleryPlayerController");
                    if (galleryPlayerControllerType != null) break;
                }
            }

            if (galleryPlayerControllerType != null)
            {
                var controller = player.GetComponent(galleryPlayerControllerType);
                results.Add($"  GalleryPlayerController: {(controller != null ? "FOUND" : "MISSING")}");
            }
            else
            {
                results.Add($"  GalleryPlayerController: TYPE NOT FOUND");
            }
        }

        // Output
        string fullReport = string.Join("\n", results);
        Debug.Log(fullReport);

        // Also print summary
        bool allGood = hasMainCamera && mainCameraEnabled && mainCameraTagCorrect &&
                      urpRendererDataValid && hasGsplatURPFeature;

        Debug.Log($"=== Phase 4 Audit Summary: {(allGood ? "✓ ALL CHECKS PASSED" : "✗ SOME CHECKS FAILED")} ===");
    }

    static string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        Transform t = obj.transform.parent;
        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        return path;
    }
}