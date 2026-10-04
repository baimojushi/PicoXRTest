using UnityEngine;

namespace GalleryRuntime
{
    /// <summary>
    /// Conservative first-pass server profile for mobile 2D streaming.
    /// Start at 1280x720/30, then raise quality only after end-to-end latency is stable.
    /// </summary>
    public sealed class ServerQualityProfile : MonoBehaviour
    {
        public int width = 1280;
        public int height = 720;
        public int targetFrameRate = 30;

        private void Awake()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
            Screen.SetResolution(width, height, false);
        }
    }
}
