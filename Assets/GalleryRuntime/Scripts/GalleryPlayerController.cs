using UnityEngine;
using UnityEngine.InputSystem;

namespace GalleryRuntime
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class GalleryPlayerController : MonoBehaviour
    {
        public RemoteControlUdpReceiver remoteInput;
        public Transform viewTransform;

        [Header("Movement")]
        public float moveSpeed = 2.2f;
        public float gravity = -18f;

        [Header("Look")]
        public float yawSpeedDegPerSec = 110f;
        public float pitchSpeedDegPerSec = 90f;
        public float minPitch = -75f;
        public float maxPitch = 75f;
        public float mouseDegPerPixel = 0.25f;

        [Header("Reset")]
        public Transform resetPose;

        private CharacterController _cc;
        private float _verticalVelocity;
        private float _yaw;
        private float _pitch;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (remoteInput == null)
                remoteInput = FindFirstObjectByType<RemoteControlUdpReceiver>();
            if (remoteInput == null)
                Debug.LogWarning("[GalleryPlayerController] RemoteControlUdpReceiver missing; WebRTC movement disabled");
            _yaw = transform.eulerAngles.y;
            if (viewTransform != null)
            {
                _pitch = NormalizePitch(viewTransform.localEulerAngles.x);
                viewTransform.tag = "MainCamera";
            }
        }

        private void Update()
        {
            Vector2 move = remoteInput != null ? remoteInput.Move : Vector2.zero;
            Vector2 look = remoteInput != null ? remoteInput.Look : Vector2.zero;

            // 新 Input System fallback（当没有 UDP 输入时）
            if (move == Vector2.zero)
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    move.x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) +
                             (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? -1f : 0f);
                    move.y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) +
                             (kb.sKey.isPressed || kb.downArrowKey.isPressed ? -1f : 0f);
                }
            }
            if (look == Vector2.zero)
            {
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    look.x = mouse.delta.x.ReadValue() * 0.1f;
                    look.y = mouse.delta.y.ReadValue() * 0.1f;
                }
            }

            _yaw += look.x * yawSpeedDegPerSec * Time.deltaTime;
            _pitch = Mathf.Clamp(
                _pitch - look.y * pitchSpeedDegPerSec * Time.deltaTime,
                minPitch, maxPitch);

            // Mouse drag is an absolute delta: the pixels travelled map straight to degrees,
            // which is what makes "drag to look around" feel 1:1 instead of speed-driven.
            if (remoteInput != null)
            {
                Vector2 mouseDelta = remoteInput.ConsumeMouseDelta();
                if (mouseDelta.sqrMagnitude > 0f)
                {
                    _yaw += mouseDelta.x * mouseDegPerPixel;
                    _pitch = Mathf.Clamp(_pitch - mouseDelta.y * mouseDegPerPixel, minPitch, maxPitch);
                }
            }

            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (viewTransform != null)
                viewTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            Vector3 planar = transform.forward * move.y + transform.right * move.x;
            planar = Vector3.ClampMagnitude(planar, 1f) * moveSpeed;

            if (_cc.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -1.5f;
            else
                _verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = planar + Vector3.up * _verticalVelocity;
            _cc.Move(velocity * Time.deltaTime);

            if (remoteInput != null && remoteInput.ResetPressedThisFrame && resetPose != null)
            {
                _cc.enabled = false;
                transform.SetPositionAndRotation(resetPose.position, resetPose.rotation);
                _cc.enabled = true;
                _yaw = transform.eulerAngles.y;
                _pitch = 0f;
                _verticalVelocity = 0f;
            }
        }

        private static float NormalizePitch(float x)
        {
            return x > 180f ? x - 360f : x;
        }
    }
}
