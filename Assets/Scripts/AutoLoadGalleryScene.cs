/// <summary>
/// Inert placeholder. The editor-only auto-play helper used to live in this runtime folder
/// as a second class also named AutoLoadGalleryScene, with an [InitializeOnLoad] static
/// constructor that reopened the scene on every assembly reload - including the reload that
/// entering Play Mode itself triggers - which made the editor play an Untitled scene.
///
/// The single supported entry point is now Assets/Editor/AutoLoadGalleryScene.cs.
/// This file is kept (rather than removed) only so its asset GUID stays stable.
/// </summary>
internal static class AutoLoadGallerySceneLegacyPlaceholder
{
}
