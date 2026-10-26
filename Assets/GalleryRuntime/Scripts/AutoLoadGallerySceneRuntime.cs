using UnityEngine;

/// <summary>
/// Runtime entry point for -executeMethod.
/// Sets a flag that Editor-side InitializeOnLoad will detect and execute the scene loading.
/// </summary>
public static class AutoLoadGallerySceneRuntime
{
    public const string SceneLoadedFlag = "AutoLoadGalleryScene_RuntimeRequested";

    /// <summary>
    /// Public entry point for -executeMethod AutoLoadGallerySceneRuntime.Run
    /// </summary>
    public static void Run()
    {
        Debug.Log("[AutoLoadGallerySceneRuntime] Run() called via executeMethod");
        // Set flag for Editor-side InitializeOnLoad to detect (using PlayerPrefs which works in both Editor and Player)
        PlayerPrefs.SetInt(SceneLoadedFlag, 1);
        PlayerPrefs.Save();
        Debug.Log("[AutoLoadGallerySceneRuntime] Flag set via PlayerPrefs, Editor will handle scene loading");
    }
}