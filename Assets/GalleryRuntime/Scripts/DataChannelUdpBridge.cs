using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Phase 4: DataChannel -> UDP Bridge
/// 将 WebRTC DataChannel 控制消息转换为现有 UDP 7777 协议
/// 浏览器 DataChannel -> Unity WebRTC -> 此桥接器 -> UDP 127.0.0.1:7777 -> RemoteControlUdpReceiver -> GalleryPlayerController
/// </summary>
public class DataChannelUdpBridge : MonoBehaviour
{
    [Header("UDP Target (existing RemoteControlUdpReceiver)")]
    public string udpHost = "127.0.0.1";
    public int udpPort = 7777;
    
    [Header("DataChannel Settings")]
    public int sendRateHz = 30;      // 本地 UDP 发送频率
    public float disconnectTimeoutSec = 0.5f;  // 断线自动清零保护
    
    private UdpClient udpClient;
    private IPEndPoint udpEndpoint;
    private readonly ConcurrentQueue<byte[]> inbox = new ConcurrentQueue<byte[]>();
    private readonly ConcurrentQueue<byte[]> outbox = new ConcurrentQueue<byte[]>();
    private CancellationTokenSource cts;
    private Thread sendThread;
    private Thread receiveThread;
    
    // 最后收到控制消息的时间
    private DateTime lastMessageTime = DateTime.MinValue;
    private bool channelOpen = false;
    private readonly object stateLock = new object();
    
    // 当前控制状态（用于定时发送）
    private float lx, ly, rx, ry;
    private bool usePressed, resetPressed;
    private bool hasPendingInput = false;
    private int packetSeq = 0;
    // 鼠标拖动像素增量（绝对位移，不速率）
    private float pendingMouseDx, pendingMouseDy;
    
    void Awake()
    {
        udpEndpoint = new IPEndPoint(IPAddress.Parse(udpHost), udpPort);
        udpClient = new UdpClient();
        udpClient.Client.ReceiveTimeout = 100;
        
        cts = new CancellationTokenSource();
        sendThread = new Thread(SendLoop) { IsBackground = true };
        receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        sendThread.Start(cts.Token);
        receiveThread.Start(cts.Token);
        
        Debug.Log($"[DataChannelBridge] Started UDP -> {udpHost}:{udpPort}");
    }
    
    /// <summary>
    /// 供 WebRTCBootstrap 调用：DataChannel 收到消息时入队
    /// </summary>
    public void EnqueueBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return;
        }

        lock (stateLock)
        {
            // A received message is itself proof the channel is open; trusting only OnOpen
            // drops all input whenever that callback never fires.
            if (!channelOpen)
            {
                channelOpen = true;
                Debug.Log("[DataChannelBridge] first message received; treating channel as open");
            }

            inbox.Enqueue(bytes);
        }
    }
    
    /// <summary>
    /// DataChannel 打开时调用
    /// </summary>
    public void OnChannelOpened()
    {
        lock (stateLock)
        {
            channelOpen = true;
            lastMessageTime = DateTime.MinValue;
            ClearInputLocked();
            while (inbox.TryDequeue(out _)) {}
        }
        Debug.Log("[DataChannelBridge] DataChannel opened");
    }
    
    /// <summary>
    /// DataChannel 关闭时调用
    /// </summary>
    public void OnChannelClosed()
    {
        lock (stateLock)
        {
            channelOpen = false;
            ClearInputLocked();
            while (inbox.TryDequeue(out _)) {}
        }
        Debug.Log("[DataChannelBridge] DataChannel closed");
        // 发送清零保护
        SendZeroInput();
    }
    
    /// <summary>
    /// 解析 DataChannel JSON 消息并更新内部状态
    /// 预期格式: {"type":"control","lx":-1..1,"ly":-1..1,"rx":-1..1,"ry":-1..1,"use":true,"reset":false}
    /// </summary>
    [Serializable]
    private class ControlPacket
    {
        public string type;
        public float lx, ly, rx, ry;
        // 鼠标拖动像素增量（绝对位移，不是速率）
        public float mdx, mdy;
        public bool use, reset;
    }

    private void ClearInputLocked()
    {
        lx = ly = rx = ry = 0f;
        usePressed = resetPressed = false;
        pendingMouseDx = 0f;
        pendingMouseDy = 0f;
        hasPendingInput = true;
    }

    void ProcessMessage(byte[] bytes)
    {
        try
        {
            var packet = JsonUtility.FromJson<ControlPacket>(Encoding.UTF8.GetString(bytes));
            if (packet == null || packet.type != "control") return; // ping MUST NOT refresh control timeout
            if (float.IsNaN(packet.lx) || float.IsNaN(packet.ly) ||
                float.IsNaN(packet.rx) || float.IsNaN(packet.ry) ||
                float.IsInfinity(packet.lx) || float.IsInfinity(packet.ly) ||
                float.IsInfinity(packet.rx) || float.IsInfinity(packet.ry)) return;
            if (float.IsNaN(packet.mdx) || float.IsNaN(packet.mdy) ||
                float.IsInfinity(packet.mdx) || float.IsInfinity(packet.mdy)) return;
            lock (stateLock)
            {
                if (!channelOpen) return;
                lx = Mathf.Clamp(packet.lx, -1f, 1f);
                ly = Mathf.Clamp(packet.ly, -1f, 1f);
                rx = Mathf.Clamp(packet.rx, -1f, 1f);
                ry = Mathf.Clamp(packet.ry, -1f, 1f);
                usePressed = packet.use;
                resetPressed = packet.reset;
                // 像素增量累加后随下一个 UDP 包发出，且只发一次。
                pendingMouseDx += packet.mdx;
                pendingMouseDy += packet.mdy;
                hasPendingInput = true;
                lastMessageTime = DateTime.UtcNow;
            }
        }
        catch (Exception e) { Debug.LogWarning($"[DataChannelBridge] Parse error: {e.Message}"); }
    }

    static float ExtractFloat(string json, string key)
    {
        string pattern = "\"" + key + "\"";
        int idx = json.IndexOf(pattern, StringComparison.Ordinal);
        if (idx < 0) return 0f;
        
        idx += pattern.Length;
        while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
        if (idx < json.Length && json[idx] == ':') idx++;
        while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
        
        int end = idx;
        while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ']') end++;
        
        string val = json.Substring(idx, end - idx).Trim();
        return float.TryParse(val, System.Globalization.NumberStyles.Float, 
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }
    
    static bool ExtractBool(string json, string key)
    {
        string pattern = "\"" + key + "\"";
        int idx = json.IndexOf(pattern, StringComparison.Ordinal);
        if (idx < 0) return false;
        
        idx += pattern.Length;
        while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
        if (idx < json.Length && json[idx] == ':') idx++;
        while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
        
        return json.Length > idx + 3 && json.Substring(idx, 4) == "true";
    }
    
    void SendLoop(object tokenObj)
    {
        var token = (CancellationToken)tokenObj;
        var interval = TimeSpan.FromSeconds(1.0 / sendRateHz);
        
        while (!token.IsCancellationRequested)
        {
            var start = DateTime.UtcNow;
            
            // 处理入站消息
            while (inbox.TryDequeue(out var bytes))
            {
                ProcessMessage(bytes);
            }
            
            // Heartbeat is deliberately ignored. Stale controls cannot remain active.
            lock (stateLock)
            {
                if (channelOpen && (DateTime.UtcNow - lastMessageTime).TotalSeconds > disconnectTimeoutSec)
                    ClearInputLocked();
                if (hasPendingInput || channelOpen) SendCurrentState();
            }

            var elapsed = DateTime.UtcNow - start;
            var remaining = interval - elapsed;
            if (remaining > TimeSpan.Zero)
                Thread.Sleep(remaining);
        }
    }
    
    void SendCurrentState()
    {
        try
        {
            // Receiver expects RemoteInputPacket: moveX/moveY/lookX/lookY/interact/reset.
            // mouseDX/mouseDY carry the absolute mouse drag in pixels.
            // Built by explicit concatenation: string.Format mis-parses an escaped "}}"
            // that follows a format item and leaks the format specifier as literal text.
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var json = "{\"seq\":" + packetSeq.ToString(ci) +
                       ",\"moveX\":" + Num(lx, ci) +
                       ",\"moveY\":" + Num(ly, ci) +
                       ",\"lookX\":" + Num(rx, ci) +
                       ",\"lookY\":" + Num(ry, ci) +
                       ",\"interact\":" + (usePressed ? "true" : "false") +
                       ",\"reset\":" + (resetPressed ? "true" : "false") +
                       ",\"mouseDX\":" + Num(pendingMouseDx, ci) +
                       ",\"mouseDY\":" + Num(pendingMouseDy, ci) + "}";
            packetSeq++;
            var bytes = Encoding.UTF8.GetBytes(json);
            udpClient.Send(bytes, bytes.Length, udpEndpoint);
            pendingMouseDx = 0f;
            pendingMouseDy = 0f;
            hasPendingInput = false;
        }
        catch (Exception e) { Debug.LogWarning($"[DataChannelBridge] UDP send error: {e.Message}"); }
    }

    // JSON has no NaN/Infinity, so non-finite values are sent as 0.
    static string Num(float v, System.Globalization.CultureInfo ci)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
        return v.ToString("F3", ci);
    }

    void SendZeroInput()
    {
        lock (stateLock) { ClearInputLocked(); SendCurrentState(); }
    }

    void ReceiveLoop(object tokenObj)
    {
        // 可选：接收 UDP 回包（如果 RemoteControlUdpReceiver 有反馈）
        var token = (CancellationToken)tokenObj;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (udpClient.Available > 0)
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    var data = udpClient.Receive(ref ep);
                    // 可选：处理反馈
                }
            }
            catch (SocketException)
            {
                // Timeout is expected
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DataChannelBridge] UDP receive error: {e.Message}");
            }
            Thread.Sleep(10);
        }
    }
    
    void OnDestroy()
    {
        cts?.Cancel();
        try { sendThread?.Join(100); } catch { }
        try { receiveThread?.Join(100); } catch { }
        
        SendZeroInput();
        udpClient?.Close();
        udpClient?.Dispose();
        Debug.Log("[DataChannelBridge] Stopped");
    }
}