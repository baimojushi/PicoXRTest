using System.IO;
using UnityEngine;

namespace GalleryRuntime
{
    /// <summary>
    /// Optional centimeter-scale correction between the visual splat root and
    /// the collision world. Start with identity; enable only after checking
    /// floor/wall contacts in Unity.
    /// </summary>
    public sealed class SceneAlignmentApplier : MonoBehaviour
    {
        public Transform visualRoot;
        public bool applyMicroCorrection = false;

        [Header("Visual -> Collision correction in Unity RUF")]
        public Vector3 correctionPosition = new Vector3(0.01376872f, -0.00836737f, 0.00299699f);
        public Vector3 correctionEulerDeg = new Vector3(-0.1499366f, 0.1095414f, 0.06496119f);

        private void Awake()
        {
            if (visualRoot == null)
                visualRoot = transform;

            if (!applyMicroCorrection)
                return;

            visualRoot.localPosition = correctionPosition;
            visualRoot.localRotation = Quaternion.Euler(correctionEulerDeg);
            Debug.Log($"[SceneAlignmentApplier] Applied micro correction pos={correctionPosition}, euler={correctionEulerDeg}");
        }
    }
}
