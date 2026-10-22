using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;

public class SetupProject
{
    [MenuItem("Tools/UnitySplats/Setup Project")]
    public static void Setup()
    {
        // URP package is already in manifest.json
        // URP asset and renderer are already configured in project
        // Gsplat URP Feature is added via the URP Renderer Data asset
        // Graphics Settings already uses URP
        
        Debug.Log("Project setup complete - URP and Gsplat are configured via assets.");
    }

    [MenuItem("Tools/UnitySplats/Setup Scene")]
    public static void SetupScene()
    {
        // 创建场景层级
        var galleryScene = new GameObject("GalleryScene");

        var sceneRoot = new GameObject("SceneRoot");
        sceneRoot.transform.SetParent(galleryScene.transform);

        var visualRoot = new GameObject("VisualRoot");
        visualRoot.transform.SetParent(sceneRoot.transform);

        var physicsRoot = new GameObject("PhysicsRoot");
        physicsRoot.transform.SetParent(sceneRoot.transform);

        // SceneAlignmentApplier
        var alignmentApplier = visualRoot.AddComponent<GalleryRuntime.SceneAlignmentApplier>();
        alignmentApplier.visualRoot = visualRoot.transform;
        alignmentApplier.applyMicroCorrection = false; // Start with identity per Gate G1

        // SplatZoneManager
        var zoneManager = visualRoot.AddComponent<GalleryRuntime.SplatZoneManager>();

        // 导入碰撞网格
        var collisionProxy = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GalleryRuntime/Collision/collision_proxy_ruf.obj");
        if (collisionProxy != null)
        {
            var collisionInstance = PrefabUtility.InstantiatePrefab(collisionProxy) as GameObject;
            collisionInstance.name = "collision_proxy_ruf";
            collisionInstance.transform.SetParent(physicsRoot.transform);

            var hiddenCollisionWorld = collisionInstance.AddComponent<GalleryRuntime.HiddenCollisionWorld>();
        }

        // 创建 Player
        var player = new GameObject("Player");
        player.transform.position = new Vector3(2.14f, -0.46f, 5.0f); // On floor with clearance

        var characterController = player.AddComponent<CharacterController>();
        characterController.radius = 0.3f;
        characterController.height = 1.7f;
        characterController.center = new Vector3(0, 0.85f, 0);
        characterController.stepOffset = 0.3f;
        characterController.skinWidth = 0.08f;
        characterController.minMoveDistance = 0.001f;
        characterController.slopeLimit = 45f;

        var udpReceiver = player.AddComponent<GalleryRuntime.RemoteControlUdpReceiver>();
        var playerController = player.AddComponent<GalleryRuntime.GalleryPlayerController>();
        var interactor = player.AddComponent<GalleryRuntime.GalleryInteractor>();

        var cameraObj = new GameObject("Camera");
        cameraObj.transform.SetParent(player.transform);
        cameraObj.transform.localPosition = new Vector3(0, 1.6f, 0);
        var camera = cameraObj.AddComponent<Camera>();

        // 设置 SplatZoneManager 引用
        var zoneManagerObj = visualRoot.GetComponent<GalleryRuntime.SplatZoneManager>();
        if (zoneManagerObj != null)
        {
            // Load zones.json directly from disk (StreamingAssets files aren't imported into AssetDatabase)
            var manifestPath = Path.Combine(Application.dataPath, "StreamingAssets/Gallery/Config/zones.json");
            TextAsset manifestAsset = null;
            if (File.Exists(manifestPath))
            {
                var jsonText = File.ReadAllText(manifestPath);
                manifestAsset = new TextAsset(jsonText);
                manifestAsset.name = "zones.json";
                Debug.Log($"Loaded zones.json from {manifestPath}");
            }
            else
            {
                Debug.LogWarning($"zones.json not found at {manifestPath}. Scene setup will continue but SplatZoneManager won't have manifest.");
            }
            var so = new SerializedObject(zoneManagerObj);
            so.FindProperty("observer").objectReferenceValue = cameraObj.transform;
            so.FindProperty("visualRoot").objectReferenceValue = visualRoot.transform;
            so.FindProperty("manifestRelativePath").stringValue = "Gallery/Config/zones.json";
            so.FindProperty("maxHighResidentZones").intValue = 1;
            so.ApplyModifiedProperties();
        }

        // 设置 GalleryPlayerController 引用
        if (playerController != null)
        {
            var so = new SerializedObject(playerController);
            so.FindProperty("viewTransform").objectReferenceValue = cameraObj.transform;
            so.ApplyModifiedProperties();
        }

        // 创建 ResetPose
        var resetPose = new GameObject("ResetPose");
        resetPose.transform.SetParent(galleryScene.transform);
        resetPose.transform.position = new Vector3(2.14f, -0.46f, 5.0f); // On floor with clearance

        // 保存场景
        var scenePath = "Assets/Scenes/GalleryScene.unity";
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);

        Debug.Log("Scene setup complete!");
    }
}