using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;

// 服务端 H.264 推流：浏览器发 offer（含 DataChannel），本端作答。
public class VideoPusher : MonoBehaviour
{
    public int width = 1280;
    public int height = 720;
    public int framerate = 30;
    public string signalingUrl = "ws://127.0.0.1:8766";
    public float turnSpeed = 75f;
    public float moveSpeed = 3f;

    Camera cam;
    VideoStreamTrack videoTrack;
    MediaStream mediaStream;
    RTCPeerConnection pc;
    RTCDataChannel control;
    bool remoteDescriptionSet;

    ClientWebSocket socket;
    CancellationTokenSource cts;
    readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
    readonly ConcurrentQueue<string> outbox = new ConcurrentQueue<string>();
    readonly List<RTCIceCandidateInit> pendingCandidates = new List<RTCIceCandidateInit>();

    // Phase 4: DataChannel -> UDP Bridge
    DataChannelUdpBridge dataChannelBridge;

    bool ready;
    bool baselineSet;
    float baseYaw;
    float basePitch;
    float yaw;
    float pitch;
    float lx, ly, rx, ry;
    bool useHeld;

    int frameCount;
    float reportClock;
    int reportFrames;
    float fps;
    int sessionCount;

    IEnumerator Start()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[WebRTC] no camera to capture");
            enabled = false;
            yield break;
        }

        // Phase 4: Get DataChannelUdpBridge reference
        dataChannelBridge = GetComponent<DataChannelUdpBridge>();
        if (dataChannelBridge == null)
        {
            dataChannelBridge = gameObject.AddComponent<DataChannelUdpBridge>();
            Debug.Log("[WebRTC] Added DataChannelUdpBridge component");
        }

        cts = new CancellationTokenSource();
        StartCoroutine(WebRTC.Update());

        videoTrack = cam.CaptureStreamTrack(width, height);
        if (videoTrack == null)
        {
            Debug.LogError("[WebRTC] CaptureStreamTrack returned null");
            yield break;
        }
        mediaStream = new MediaStream();
        mediaStream.AddTrack(videoTrack);
        ready = true;

        Debug.Log($"[WebRTC] capture {width}x{height}@{framerate} camera={cam.name} signaling={signalingUrl}");
        _ = Task.Run(ConnectLoop);
    }

    void Update()
    {
        if (!ready) return;

        frameCount++;
        reportFrames++;
        reportClock += Time.deltaTime;
        if (reportClock >= 1f)
        {
            fps = reportFrames / reportClock;
            reportClock = 0f;
            reportFrames = 0;
            SendState();
        }

        while (inbox.TryDequeue(out var message))
            HandleSignal(message);

        ApplyControl();
    }

    // ---------- control ----------

    void ApplyControl()
    {
        if (!baselineSet)
        {
            baseYaw = cam.transform.eulerAngles.y;
            basePitch = cam.transform.eulerAngles.x;
            baselineSet = true;
        }

        if (Mathf.Abs(rx) > 0.001f || Mathf.Abs(ry) > 0.001f)
        {
            yaw += rx * turnSpeed * Time.deltaTime;
            pitch = Mathf.Clamp(pitch - ry * turnSpeed * Time.deltaTime, -80f, 80f);
        }
        cam.transform.rotation = Quaternion.Euler(basePitch + pitch, baseYaw + yaw, 0f);

        if (Mathf.Abs(lx) > 0.001f || Mathf.Abs(ly) > 0.001f)
        {
            Vector3 move = cam.transform.forward * ly + cam.transform.right * lx;
            cam.transform.position += move * moveSpeed * Time.deltaTime;
        }

        if (useHeld)
        {
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 20f))
            {
                Debug.Log($"[WebRTC] USE hit {hit.collider.name} at {hit.point}");
                useHeld = false;
            }
        }
    }

    void SendState()
    {
        if (control == null || control.ReadyState != RTCDataChannelState.Open) return;
        var p = cam.transform.position;
        var e = cam.transform.eulerAngles;
        string json = "{\"type\":\"state\",\"fps\":" + fps.ToString("F1") +
                      ",\"pos\":[" + p.x.ToString("F2") + "," + p.y.ToString("F2") + "," + p.z.ToString("F2") + "]" +
                      ",\"rot\":[" + e.x.ToString("F1") + "," + e.y.ToString("F1") + "]" +
                      ",\"frames\":" + frameCount +
                      ",\"session\":" + sessionCount +
                      ",\"latencyMs\":" + lastRttMs + "}";
        try { control.Send(json); } catch (Exception e2) { Debug.LogWarning($"[WebRTC] state send: {e2.Message}"); }
    }

    int lastRttMs = -1;

    // ---------- signaling ----------

    void HandleSignal(string json)
    {
        string type = JsonField(json, "type");
        switch (type)
        {
            case "offer":
            {
                string sdp = JsonField(json, "sdp");
                if (string.IsNullOrEmpty(sdp))
                {
                    Debug.LogWarning("[WebRTC] offer without sdp");
                    return;
                }
                StartCoroutine(AcceptOffer(sdp));
                break;
            }
            case "candidate":
            {
                var init = new RTCIceCandidateInit
                {
                    candidate = JsonField(json, "candidate"),
                    sdpMid = JsonField(json, "sdpMid"),
                    sdpMLineIndex = JsonInt(json, "sdpMLineIndex")
                };
                if (string.IsNullOrEmpty(init.candidate)) return;
                if (pc == null || !remoteDescriptionSet)
                {
                    pendingCandidates.Add(init);
                    return;
                }
                try { pc.AddIceCandidate(new RTCIceCandidate(init)); }
                catch (Exception e) { Debug.LogWarning($"[WebRTC] AddIceCandidate: {e.Message}"); }
                break;
            }
            case "ping":
            {
                long t0 = JsonLong(json, "t0");
                if (t0 > 0)
                {
                    lastRttMs = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t0);
                }
                // 直接回 pong（服务端会转发回浏览器）
                var pong = "{\"type\":\"pong\",\"t0\":" + t0 + ",\"t1\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}";
                if (control != null && control.ReadyState == RTCDataChannelState.Open)
                    control.Send(pong);
                break;
            }
            default:
                break;
        }
    }

    IEnumerator AcceptOffer(string sdp)
    {
        ClosePeer();

        pc = new RTCPeerConnection();
        remoteDescriptionSet = false;
        pendingCandidates.Clear();

        pc.OnIceCandidate = candidate =>
        {
            if (candidate == null || string.IsNullOrEmpty(candidate.Candidate)) return;
            string json = "{\"type\":\"candidate\",\"candidate\":" + JsonQuote(candidate.Candidate) +
                          ",\"sdpMid\":" + JsonQuote(candidate.SdpMid) +
                          ",\"sdpMLineIndex\":" + (candidate.SdpMLineIndex ?? 0) + "}";
            outbox.Enqueue(json);
        };
        pc.OnIceConnectionChange = state => Debug.Log($"[WebRTC] ice state: {state}");
        pc.OnDataChannel = channel =>
        {
            Debug.Log($"[WebRTC] datachannel '{channel.Label}' opened");
            
            if (channel.Label == "control")
            {
                control = channel;
                
                // Phase 4: Bridge DataChannel to UDP for GalleryPlayerController
                if (dataChannelBridge != null)
                {
                    channel.OnOpen = () => 
                    {
                        Debug.Log("[WebRTC] control datachannel open");
                        dataChannelBridge.OnChannelOpened();
                    };
                    channel.OnClose = () => 
                    {
                        Debug.Log("[WebRTC] control datachannel closed");
                        dataChannelBridge.OnChannelClosed();
                    };
                    channel.OnMessage = bytes => 
                    {
                        // Forward to bridge
                        dataChannelBridge.EnqueueBytes(bytes);
                        // Also handle locally for backward compat
                        OnControlMessage(bytes);
                    };
                    Debug.Log("[WebRTC] control datachannel bridged to UDP");
                }
                else
                {
                    // Fallback: original behavior
                    channel.OnOpen = () => Debug.Log("[WebRTC] datachannel open");
                    channel.OnClose = () => Debug.Log("[WebRTC] datachannel closed");
                    channel.OnMessage = OnControlMessage;
                }
            }
            else
            {
                // Other datachannels (if any)
                channel.OnOpen = () => Debug.Log($"[WebRTC] datachannel '{channel.Label}' open");
                channel.OnClose = () => Debug.Log($"[WebRTC] datachannel '{channel.Label}' closed");
                channel.OnMessage = OnControlMessage;
            }
        };

        pc.AddTrack(videoTrack, mediaStream);

        var remote = new RTCSessionDescription { type = RTCSdpType.Offer, sdp = sdp };
        var setRemote = pc.SetRemoteDescription(ref remote);
        yield return setRemote;
        if (setRemote.IsError)
        {
            Debug.LogError("[WebRTC] SetRemoteDescription failed: " + setRemote.Error.message);
            yield break;
        }
        remoteDescriptionSet = true;
        Debug.Log($"[WebRTC] offer accepted, {pendingCandidates.Count} early candidates");

        foreach (var candidate in pendingCandidates)
        {
            try { pc.AddIceCandidate(new RTCIceCandidate(candidate)); }
            catch (Exception e) { Debug.LogWarning($"[WebRTC] AddIceCandidate(queued): {e.Message}"); }
        }
        pendingCandidates.Clear();

        var answerOp = pc.CreateAnswer();
        yield return answerOp;
        if (answerOp.IsError)
        {
            Debug.LogError("[WebRTC] CreateAnswer failed: " + answerOp.Error.message);
            yield break;
        }

        var answer = answerOp.Desc;
        var setLocal = pc.SetLocalDescription(ref answer);
        yield return setLocal;
        if (setLocal.IsError)
        {
            Debug.LogError("[WebRTC] SetLocalDescription failed: " + setLocal.Error.message);
            yield break;
        }

        outbox.Enqueue("{\"type\":\"answer\",\"sdp\":" + JsonQuote(answer.sdp) + "}");
        sessionCount++;
        Debug.Log($"[WebRTC] answer sent (session {sessionCount})");
    }

    void OnControlMessage(byte[] bytes)
    {
        string json = Encoding.UTF8.GetString(bytes);
        string type = JsonField(json, "type");
        if (type == "control")
        {
            lx = JsonFloat(json, "lx");
            ly = JsonFloat(json, "ly");
            rx = JsonFloat(json, "rx");
            ry = JsonFloat(json, "ry");
            if (JsonBool(json, "use")) useHeld = true;
        }
        else if (type == "ping")
        {
            long t0 = JsonLong(json, "t0");
            Debug.Log($"[WebRTC] ping received t0={t0}, lastRttMs={lastRttMs}");
            if (t0 > 0)
            {
                lastRttMs = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t0);
                Debug.Log($"[WebRTC] rtt computed={lastRttMs}ms");
            }
            var pong = "{\"type\":\"pong\",\"t0\":" + t0 + ",\"t1\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}";
            try { control?.Send(pong); Debug.Log("[WebRTC] pong sent"); } catch { }
        }
    }

    void ClosePeer()
    {
        if (control != null)
        {
            try { control.Close(); } catch { }
            control = null;
        }
        if (pc != null)
        {
            try { pc.Close(); } catch { }
            pc.Dispose();
            pc = null;
        }
    }

    async Task ConnectLoop()
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                using (var ws = new ClientWebSocket())
                {
                    socket = ws;
                    await ws.ConnectAsync(new Uri(signalingUrl), cts.Token);
                    Debug.Log("[WebRTC] signaling connected " + signalingUrl);
                    outbox.Enqueue("{\"type\":\"hello\",\"role\":\"unity\"}");
                    var send = SendLoop(ws);
                    await ReceiveLoop(ws);
                    try { ws.Abort(); } catch { }
                    try { await send; } catch { }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WebRTC] signaling error: " + e.Message);
            }
            socket = null;
            if (cts.IsCancellationRequested) break;
            try { await Task.Delay(2000, cts.Token); } catch { break; }
        }
    }

    async Task ReceiveLoop(ClientWebSocket ws)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            if (result.MessageType == WebSocketMessageType.Close) break;
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage) continue;
            var message = sb.ToString();
            sb.Length = 0;
            if (message.Length > 0) inbox.Enqueue(message);
        }
    }

    async Task SendLoop(ClientWebSocket ws)
    {
        while (!cts.IsCancellationRequested)
        {
            if (ws.State != WebSocketState.Open) return;
            if (outbox.TryDequeue(out var message))
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
            }
            else
            {
                await Task.Delay(8, cts.Token);
            }
        }
    }

    void OnDestroy()
    {
        if (cts != null) cts.Cancel();
        ClosePeer();
        if (videoTrack != null) videoTrack.Dispose();
        if (mediaStream != null) mediaStream.Dispose();
    }

    // ---------- minimal JSON ----------

    static string JsonQuote(string value)
    {
        if (value == null) return "null";
        var sb = new StringBuilder(value.Length + 8);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    static int FindValue(string json, string key)
    {
        string needle = "\"" + key + "\"";
        int i = 0;
        while ((i = json.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            int j = i + needle.Length;
            while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
            if (j < json.Length && json[j] == ':')
            {
                j++;
                while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                return j;
            }
            i = j;
        }
        return -1;
    }

    public static string JsonField(string json, string key)
    {
        int i = FindValue(json, key);
        if (i < 0 || i >= json.Length) return null;
        if (json[i] == '"')
        {
            var sb = new StringBuilder();
            for (int k = i + 1; k < json.Length; k++)
            {
                char c = json[k];
                if (c == '"') break;
                if (c == '\\' && k + 1 < json.Length)
                {
                    char n = json[++k];
                    switch (n)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (k + 4 < json.Length)
                            {
                                sb.Append((char)Convert.ToInt32(json.Substring(k + 1, 4), 16));
                                k += 4;
                            }
                            break;
                        default: sb.Append(n); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
        int end = i;
        while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ']') end++;
        return json.Substring(i, end - i).Trim();
    }

    public static float JsonFloat(string json, string key)
    {
        var raw = JsonField(json, key);
        return float.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }

    public static int JsonInt(string json, string key)
    {
        var raw = JsonField(json, key);
        return int.TryParse(raw, out var v) ? v : 0;
    }

    public static long JsonLong(string json, string key)
    {
        var raw = JsonField(json, key);
        return long.TryParse(raw, out var v) ? v : 0L;
    }

    public static bool JsonBool(string json, string key)
    {
        var raw = JsonField(json, key);
        return raw == "true";
    }
}

public static class WebRTCBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        var args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "-webrtc") < 0) return;

        int port = 8766;
        int idx = Array.IndexOf(args, "-webrtc-port");
        if (idx >= 0 && idx + 1 < args.Length) int.TryParse(args[idx + 1], out port);

        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[WebRTC] bootstrap: no Camera.main");
            return;
        }

        var pusher = cam.gameObject.AddComponent<VideoPusher>();
        pusher.signalingUrl = $"ws://127.0.0.1:{port}";
        Debug.Log($"[WebRTC] bootstrap attached to '{cam.name}', signaling {pusher.signalingUrl}");
    }
}
