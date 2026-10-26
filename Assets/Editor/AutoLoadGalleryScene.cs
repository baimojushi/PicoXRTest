using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Loads GalleryScene, opens the Game view, and enters Play Mode once per editor session.
///
/// The work is deferred until the editor is idle: doing it from an [InitializeOnLoad] static
/// constructor races the assembly reload that entering Play Mode itself triggers, which left
/// the editor playing an Untitled scene. Two independent post-reload hooks are installed
/// because a hook registered during the reload can be discarded when the reload finalises.
/// </summary>
[InitializeOnLoad]
public static class AutoLoadGalleryScene
{
    private const string ScenePath = "Assets/Scenes/GalleryScene.unity";
    private const string SessionKey = "AutoLoadGalleryScene.EnteredPlayMode";
    private const string LegacyRuntimeFlag = "AutoLoadGalleryScene_RuntimeRequested";

    private static bool finished;
    private static bool hooksInstalled;
    private static bool waitedOnce;

    static AutoLoadGalleryScene()
    {
        Debug.Log($"[AutoLoadGalleryScene] static ctor: isPlaying={EditorApplication.isPlaying} willChangePlaymode={EditorApplication.isPlayingOrWillChangePlaymode} isUpdating={EditorApplication.isUpdating} isCompiling={EditorApplication.isCompiling} sessionDone={SessionState.GetBool(SessionKey, false)}");
        InstallHooks();
    }

    private static void InstallHooks()
    {
        if (hooksInstalled)
        {
            return;
        }

        hooksInstalled = true;
        EditorApplication.delayCall += TryRun;
        EditorApplication.update += TryRun;
        Debug.Log("[AutoLoadGalleryScene] post-reload hooks installed (delayCall + update)");
    }

    private static void Uninstall()
    {
        if (!hooksInstalled)
        {
            return;
        }

        hooksInstalled = false;
        EditorApplication.delayCall -= TryRun;
        EditorApplication.update -= TryRun;
        Debug.Log("[AutoLoadGalleryScene] hooks removed");
    }

    private static void TryRun()
    {
        if (finished || SessionState.GetBool(SessionKey, false))
        {
            Uninstall();
            return;
        }

        // Entering Play Mode triggers an assembly reload that re-runs the static
        // constructor; never start a second transition from inside that reload.
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log("[AutoLoadGalleryScene] TryRun: play mode already in progress, standing down");
            Uninstall();
            return;
        }

        if (EditorApplication.isUpdating || EditorApplication.isCompiling)
        {
            if (!waitedOnce)
            {
                waitedOnce = true;
                Debug.Log("[AutoLoadGalleryScene] TryRun: waiting for isUpdating/isCompiling to clear");
            }
            return;
        }

        finished = true;
        Uninstall();
        SessionState.SetBool(SessionKey, true);

        if (PlayerPrefs.GetInt(LegacyRuntimeFlag, 0) == 1)
        {
            PlayerPrefs.SetInt(LegacyRuntimeFlag, 0);
            PlayerPrefs.Save();
        }

        RunSynchronous();
    }

    private static void RunSynchronous()
    {
        Debug.Log("[AutoLoadGalleryScene] ========== RunSynchronous START ==========");
        try
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogError($"[AutoLoadGalleryScene] Scene file NOT FOUND: {ScenePath}");
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = new List<GameObject>();
            scene.GetRootGameObjects(roots);
            Debug.Log($"[AutoLoadGalleryScene] Opened scene: {scene.name} (path: {scene.path}, loaded: {scene.isLoaded}), roots: {roots.Count}");
            foreach (var go in roots)
            {
                Debug.Log($"[AutoLoadGalleryScene]   Root: {go.name} (active: {go.activeInHierarchy})");
            }

            EnsureRunInBackground();
            EnsureGameView();

            Debug.Log($"[AutoLoadGalleryScene] Active scene before play: {EditorSceneManager.GetActiveScene().name}");
            Debug.Log("[AutoLoadGalleryScene] Calling EnterPlaymode()...");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e)
        {
            Debug.LogError($"[AutoLoadGalleryScene] EXCEPTION: {e}");
        }
        Debug.Log("[AutoLoadGalleryScene] ========== RunSynchronous END ==========");
    }

    /// <summary>
    /// A browser client drives this stream and holds keyboard focus while it does, which
    /// stops the Unity editor's player loop (and its rendering) unless Run In Background is
    /// on - that made remote control look dead and froze the video.
    /// </summary>
    private static void EnsureRunInBackground()
    {
        if (PlayerSettings.runInBackground)
        {
            Debug.Log("[AutoLoadGalleryScene] Run In Background already enabled");
            return;
        }

        PlayerSettings.runInBackground = true;
        AssetDatabase.SaveAssets();
        Debug.Log("[AutoLoadGalleryScene] Enabled Run In Background (player loop keeps running while another window has focus)");
    }

    /// <summary>
    /// A camera with no active view renders nothing, and Unity WebRTC captures the frames
    /// the camera renders - so Play Mode without a Game view streams no video at all.
    /// </summary>
    private static void EnsureGameView()
    {
        var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
        if (gameViewType != null)
        {
            var window = EditorWindow.GetWindow(gameViewType);
            if (window != null)
            {
                window.Show();
                window.Focus();
                Debug.Log("[AutoLoadGalleryScene] Game view open and focused");
                return;
            }
        }

        bool opened = EditorApplication.ExecuteMenuItem("Window/General/Game");
        Debug.Log($"[AutoLoadGalleryScene] Game view opened via menu item: {opened}");
    }

    /// <summary>
    /// Public entry point for -executeMethod AutoLoadGalleryScene.Run
    /// </summary>
    public static void Run()
    {
        Debug.Log("[AutoLoadGalleryScene] Run() called via executeMethod");
        SessionState.SetBool(SessionKey, true);
        RunSynchronous();
    }
}
