using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace GalleryRuntime
{
    [Serializable]
    public class RemoteInputPacket
    {
        public int seq;
        public float moveX;
        public float moveY;
        public float lookX;
        public float lookY;
        public float mouseDX;
        public float mouseDY;
        public bool interact;
        public bool reset;
    }

    /// <summary>
    /// Receives the latest mobile-controller state over UDP.
    /// Web browser -> WebSocket gateway -> UDP -> Unity.
    /// JSON parsing is intentionally done on Unity's main thread.
    /// </summary>
    public sealed class RemoteControlUdpReceiver : MonoBehaviour
    {
        public int udpPort = 7777;
        public float inputTimeoutSeconds = 0.5f;
        public bool verboseLogging = false;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public Vector2 MouseDelta { get; private set; }
        public bool InteractPressedThisFrame { get; private set; }
        public bool ResetPressedThisFrame { get; private set; }

        /// <summary>
        /// Returns the mouse drag accumulated since the last call and clears it, so each
        /// pixel delta is applied exactly once even if several packets land in one frame.
        /// </summary>
        public Vector2 ConsumeMouseDelta()
        {
            var delta = MouseDelta;
            MouseDelta = Vector2.zero;
            return delta;
        }

        private UdpClient _udp;
        private Thread _thread;
        private volatile bool _running;
        private readonly object _gate = new object();
        private readonly Queue<string> _pendingRaw = new Queue<string>();
        private float _lastReceiveTime = -999f;
        private bool _previousInteract;
        private bool _previousReset;

        private void OnEnable()
        {
            StartReceiver();
        }

        private void OnDisable()
        {
            StopReceiver();
        }

        private void StartReceiver()
        {
            if (_running)
                return;

            _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, udpPort));
            _running = true;
            _thread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "GalleryRemoteInputUDP"
            };
            _thread.Start();
            Debug.Log($"[RemoteControlUdpReceiver] Listening UDP :{udpPort}");
        }

        private void ReceiveLoop()
        {
            var ep = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    var data = _udp.Receive(ref ep);
                    var raw = Encoding.UTF8.GetString(data);
                    lock (_gate)
                        _pendingRaw.Enqueue(raw);
                }
                catch (SocketException)
                {
                    if (!_running) break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    if (_running)
                        Debug.LogWarning($"[RemoteControlUdpReceiver] {e.Message}");
                }
            }
        }

        private void Update()
        {
            InteractPressedThisFrame = false;
            ResetPressedThisFrame = false;

            List<string> batch = null;
            lock (_gate)
            {
                if (_pendingRaw.Count > 0)
                {
                    batch = new List<string>(_pendingRaw);
                    _pendingRaw.Clear();
                }
            }

            if (batch != null)
            {
                foreach (var raw in batch)
                {
                    if (string.IsNullOrEmpty(raw)) continue;
                    try
                    {
                        var p = JsonUtility.FromJson<RemoteInputPacket>(raw);
                        Move = Vector2.ClampMagnitude(new Vector2(p.moveX, p.moveY), 1f);
                        Look = Vector2.ClampMagnitude(new Vector2(p.lookX, p.lookY), 1f);

                        // Mouse drag is an absolute pixel delta, so packets that arrive within
                        // one frame must add up instead of overwriting each other.
                        MouseDelta += new Vector2(p.mouseDX, p.mouseDY);

                        InteractPressedThisFrame |= p.interact && !_previousInteract;
                        ResetPressedThisFrame |= p.reset && !_previousReset;
                        _previousInteract = p.interact;
                        _previousReset = p.reset;
                        _lastReceiveTime = Time.unscaledTime;

                        if (verboseLogging && p.seq % 60 == 0)
                            Debug.Log($"[RemoteInput] seq={p.seq} move={Move} look={Look} mouse={MouseDelta}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[RemoteControlUdpReceiver] Bad packet: {e.Message} raw='{raw}'");
                    }
                }
            }

            if (Time.unscaledTime - _lastReceiveTime > inputTimeoutSeconds)
            {
                Move = Vector2.zero;
                Look = Vector2.zero;
                MouseDelta = Vector2.zero;
                _previousInteract = false;
                _previousReset = false;
            }
        }

        private void StopReceiver()
        {
            _running = false;
            try { _udp?.Close(); } catch { }
            _udp = null;

            if (_thread != null && _thread.IsAlive)
                _thread.Join(250);
            _thread = null;
        }
    }
}
