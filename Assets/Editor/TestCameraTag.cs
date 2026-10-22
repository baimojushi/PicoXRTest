using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class TestCameraTag
{
    [MenuItem("Tools/Gallery/Test Camera Tag")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/GalleryScene.unity");
        Debug.Log($"Scene loaded: {scene.name}");
        
        var player = GameObject.Find("Player");
        if (player == null) { Debug.LogError("Player not found"); return; }
        
        var controller = player.GetComponent<GalleryRuntime.GalleryPlayerController>();
        if (controller == null) { Debug.LogError("GalleryPlayerController not found"); return; }
        
        // 手动触发 Awake
        controller.SendMessage("Awake", SendMessageOptions.DontRequireReceiver);
        
        var camera = GameObject.Find("Camera");
        if (camera == null) { Debug.LogError("Camera not found"); return; }
        
        Debug.Log($"Camera tag after Awake: '{camera.tag}' (should be 'MainCamera')");
        
        // 验证是否为主相机
        var mainCams = Camera.allCameras;
        Debug.Log($"All cameras: {mainCams.Length}");
        foreach (var c in mainCams)
        {
            Debug.Log($"  Camera: {c.name}, tag={c.tag}, enabled={c.enabled}, depth={c.depth}");
        }
    }
}