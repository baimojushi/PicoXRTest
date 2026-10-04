using UnityEngine;

public class PlayModeDebugger : MonoBehaviour
{
    void Awake()
    {
        Debug.Log("[PlayModeDebugger] Awake - Play Mode started");
    }

    void Start()
    {
        Debug.Log("[PlayModeDebugger] Start");
        
        // Check all key objects
        var manager = FindObjectOfType<GalleryRuntime.SplatZoneManager>();
        if (manager != null) Debug.Log("[PlayModeDebugger] SplatZoneManager found");
        else Debug.LogError("[PlayModeDebugger] SplatZoneManager NOT found!");

        var hcw = FindObjectOfType<GalleryRuntime.HiddenCollisionWorld>();
        if (hcw != null) Debug.Log("[PlayModeDebugger] HiddenCollisionWorld found");
        else Debug.LogError("[PlayModeDebugger] HiddenCollisionWorld NOT found!");

        var visualRoot = GameObject.Find("VisualRoot");
        if (visualRoot != null) Debug.Log("[PlayModeDebugger] VisualRoot found, children: " + visualRoot.transform.childCount);
        else Debug.LogError("[PlayModeDebugger] VisualRoot NOT found!");

        var camera = GameObject.Find("Camera");
        if (camera != null) Debug.Log("[PlayModeDebugger] Camera found, tag: " + camera.tag);
        else Debug.LogError("[PlayModeDebugger] Camera NOT found!");

        var player = GameObject.Find("Player");
        if (player != null) Debug.Log("[PlayModeDebugger] Player found at: " + player.transform.position);
        else Debug.LogError("[PlayModeDebugger] Player NOT found!");

        // Check zones.json path
        var manifestPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Gallery/Config/zones.json");
        Debug.Log("[PlayModeDebugger] Manifest path: " + manifestPath + " exists: " + System.IO.File.Exists(manifestPath));
        
        var globalBasePath = System.IO.Path.Combine(Application.streamingAssetsPath, "Gallery/Splats/global/gallery_base_low.ply");
        Debug.Log("[PlayModeDebugger] Global base path: " + globalBasePath + " exists: " + System.IO.File.Exists(globalBasePath));
    }

    void Update()
    {
        // Check if SplatZoneManager loaded something after a few frames
        if (Time.frameCount == 10)
        {
            var visualRoot = GameObject.Find("VisualRoot");
            if (visualRoot != null) 
                Debug.Log("[PlayModeDebugger] Frame 10 - VisualRoot children: " + visualRoot.transform.childCount);
        }
        
        if (Time.frameCount == 60)
        {
            var visualRoot = GameObject.Find("VisualRoot");
            if (visualRoot != null) 
                Debug.Log("[PlayModeDebugger] Frame 60 - VisualRoot children: " + visualRoot.transform.childCount);
            
            var manager = FindObjectOfType<GalleryRuntime.SplatZoneManager>();
            if (manager != null) 
                Debug.Log("[PlayModeDebugger] Frame 60 - SplatZoneManager still alive");
        }
    }
}