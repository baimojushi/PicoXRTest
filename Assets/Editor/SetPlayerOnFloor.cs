using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public class SetPlayerOnFloor
{
    [MenuItem("Tools/Gallery/Set Player On Floor")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/GalleryScene.unity");
        
        // Find collision world
        var hcw = GameObject.FindObjectOfType<GalleryRuntime.HiddenCollisionWorld>();
        if (hcw == null) { Debug.LogError("HiddenCollisionWorld not found!"); return; }
        var root = hcw.transform.root;
        hcw.BuildHiddenColliders();

        // Get player
        var player = GameObject.Find("Player");
        var cc = player.GetComponent<CharacterController>();
        if (cc == null) { Debug.LogError("CharacterController not found!"); return; }

        // Find floor Y at gallery center (2.14, 7.71) - the collision mesh center
        Vector3 testPos = new Vector3(2.14f, 10f, 7.71f);
        var ray = new Ray(testPos, Vector3.down);
        var hits = Physics.RaycastAll(ray, 20f);
        
        float floorY = -1.28f; // Known floor level from earlier tests
        foreach (var hit in hits)
        {
            if (hit.collider.name != "Player" && hit.normal.y > 0.7f && hit.distance > 0.1f)
            {
                floorY = hit.point.y;
                Debug.Log($"Found floor at {hit.point} normal={hit.normal}");
                break;
            }
        }

        // Set player position on floor - move slightly back from center for wall clearance
        float playerY = floorY + 0.05f + cc.center.y; // floor + min_dist + center_y
        Vector3 newPos = new Vector3(2.14f, playerY, 5.0f); // Move back on Z for clearance
        
        cc.enabled = false;
        player.transform.position = newPos;
        cc.enabled = true;

        // Also update ResetPose
        var resetPose = GameObject.Find("ResetPose");
        if (resetPose != null)
            resetPose.transform.position = newPos;

        Debug.Log($"Player moved to floor: {newPos} (floorY={floorY:F2})");
        
        // Mark scene as dirty and save
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Scene saved!");
    }
}