using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEngine;
using GalleryRuntime;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>
/// Phase 4 WebRTC Setup - Creates a test scene with Camera.main, Player, and SplatZoneManager
/// for end-to-end WebRTC testing.
/// </summary>
public class Phase4WebRTCSetup : MonoBehaviour
{
    public string zoneId = "artwork_01";
    public float preloadDistance = 16f;
    public float switchDistance = 4f;
    public float unloadDistance = 12f;

    Camera mainCam;
    GameObject playerObj;
    GameObject playerCamObj;
    SplatZoneManager manager;

    void Start()
    {
        Debug.Log("[Phase4Setup] Starting WebRTC test scene setup");
        _coroutine = StartCoroutine(SetupScene());
    }

    Coroutine _coroutine;

    void Update()
    {
        if (_coroutine != null)
        {
            try
            {
                StopCoroutine(_coroutine);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Phase4Setup] StopCoroutine error: {e.Message}");
            }
            _coroutine = null;
        }
    }

    IEnumerator SetupScene()
    {
        // Create Player + Camera
        playerObj = new GameObject("Player");
        playerObj.tag = "Player";
        playerObj.transform.position = new Vector3(100f, 1.6f, 100f);

        playerCamObj = new GameObject("Main Camera");
        playerCamObj.tag = "MainCamera";
        playerCamObj.transform.SetParent(playerObj.transform, false);
        playerCamObj.transform.position = new Vector3(100f, 1.6f, 100f);

        mainCam = playerCamObj.GetComponent<Camera>();
        if (mainCam == null) mainCam = playerCamObj.AddComponent<Camera>();
        mainCam.cameraType = CameraType.Game;
        mainCam.clearFlags = CameraClearFlags.SolidColor;
        mainCam.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        mainCam.fieldOfView = 60f;
        mainCam.nearClipPlane = 0.03f;
        mainCam.farClipPlane = 500f;

        // Create SplatZoneManager
        var managerObj = new GameObject("SplatZoneManager");
        managerObj.SetActive(false);
        manager = managerObj.GetComponent<SplatZoneManager>();
        if (manager == null) manager = managerObj.AddComponent<SplatZoneManager>();

        // Configure zones via public SetZones method
        var zoneConfigType = typeof(SplatZoneManager).Assembly.GetType("Gallery3DGS.Streaming.SplatZoneConfig");
        if (zoneConfigType != null)
        {
            var zoneConfig = Activator.CreateInstance(zoneConfigType);
            zoneConfigType.GetField("zoneId")?.SetValue(zoneConfig, zoneId);
            zoneConfigType.GetField("displayName")?.SetValue(zoneConfig, "Main Artwork");
            zoneConfigType.GetField("enabled")?.SetValue(zoneConfig, true);
            zoneConfigType.GetField("centerWorld")?.SetValue(zoneConfig, new Vector3(3.57f, -0.12f, 5.28f));
            zoneConfigType.GetField("highAssetName")?.SetValue(zoneConfig, "artwork_high_100k.ply");
            zoneConfigType.GetField("shDegree")?.SetValue(zoneConfig, 0);
            zoneConfigType.GetField("preloadDistanceM")?.SetValue(zoneConfig, preloadDistance);
            zoneConfigType.GetField("switchDistanceM")?.SetValue(zoneConfig, switchDistance);
            zoneConfigType.GetField("unloadDistanceM")?.SetValue(zoneConfig, unloadDistance);

            var zonesArray = Array.CreateInstance(zoneConfigType, 1);
            zonesArray.SetValue(zoneConfig, 0);

            var setZonesMethod = typeof(SplatZoneManager).GetMethod("SetZones");
            setZonesMethod?.Invoke(manager, new object[] { zonesArray });
        }

        // Override global low asset name
        var globalLowField = typeof(SplatZoneManager).GetField("globalLowAssetName", BindingFlags.NonPublic | BindingFlags.Instance);
        globalLowField?.SetValue(manager, "gallery_global_low_50k.ply");

        var maxHighField = typeof(SplatZoneManager).GetField("maxConcurrentHighZones", BindingFlags.NonPublic | BindingFlags.Instance);
        maxHighField?.SetValue(manager, 1);

        // Set observer BEFORE activating manager. SplatZoneManager loads its zones from the
        // StreamingAssets manifest in its own Start(), so no explicit init call is required.
        manager.observer = playerObj.transform;

        // Now activate the manager
        managerObj.SetActive(true);
        yield return null;

        Debug.Log("[Phase4Setup] Scene setup complete");
    }

    void OnDestroy()
    {
        if (playerCamObj != null) Object.Destroy(playerCamObj);
        if (playerObj != null) Object.Destroy(playerObj);
        if (manager != null && manager.enabled) Object.Destroy(manager);
    }
}