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
    public float disconnectTimeoutSec = 5f;  // 断线自动清零保护
    
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
        if (bytes != null && bytes.Length > 0)
        {
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
            lastMessageTime = DateTime.UtcNow;
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
        }
        Debug.Log("[DataChannelBridge] DataChannel closed");
        // 发送清零保护
        SendZeroInput();
    }
    
    /// <summary>
    /// 解析 DataChannel JSON 消息并更新内部状态
    /// 预期格式: {"type":"control","lx":-1..1,"ly":-1..1,"rx":-1..1,"ry":-1..1,"use":true,"reset":false}
    /// </summary>
    void ProcessMessage(byte[] bytes)
    {
        try
        {
            string json = Encoding.UTF8.GetString(bytes);
            
            // 简单 JSON 解析（避免引入 Newtonsoft）
            if (json.Contains("\"type\":\"control\"") || json.Contains("\"type\":\"control\""))
            {
                lx = ExtractFloat(json, "lx");
                ly = ExtractFloat(json, "ly");
                rx = ExtractFloat(json, "rx");
                ry = ExtractFloat(json, "ry");
                usePressed = ExtractBool(json, "use");
                resetPressed = ExtractBool(json, "reset");
                
                hasPendingInput = true;
                lastMessageTime = DateTime.UtcNow;
            }
            else if (json.Contains("\"type\":\"ping\""))
            {
                // 心跳回复（可选）
                lastMessageTime = DateTime.UtcNow;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DataChannelBridge] Parse error: {e.Message}");
        }
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
            
            // 断线保护：超过超时未收到消息，自动清零
            lock (stateLock)
            {
                if (channelOpen && (DateTime.UtcNow - lastMessageTime).TotalSeconds > disconnectTimeoutSec)
                {
                    Debug.LogWarning("[DataChannelBridge] Control timeout, sending zero input");
                    lx = ly = rx = ry = 0f;
                    usePressed = resetPressed = false;
                    hasPendingInput = true;
                }
            }
            
            // 发送当前状态（即使没变也定时发，保持心跳）
            if (hasPendingInput || channelOpen)
            {
                SendCurrentState();
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
            // UDP 协议格式：与现有 RemoteControlUdpReceiver 兼容
            // {"lx":0.5,"ly":-1.0,"rx":0.0,"ry":0.2,"use":false,"reset":true}
            var json = $"{{\"lx\":{lx:F3},\"ly\":{ly:F3},\"rx\":{rx:F3},\"ry\":{ry:F3},\"use\":{usePressed.ToString().ToLower()},\"reset\":{resetPressed.ToString().ToLower()}}}";
            var bytes = Encoding.UTF8.GetBytes(json);
            udpClient.Send(bytes, bytes.Length, udpEndpoint);
            hasPendingInput = false;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DataChannelBridge] UDP send error: {e.Message}");
        }
    }
    
    void SendZeroInput()
    {
        try
        {
            var json = "{\"lx\":0,\"ly\":0,\"rx\":0,\"ry\":0,\"use\":false,\"reset\":false}";
            var bytes = Encoding.UTF8.GetBytes(json);
            udpClient.Send(bytes, bytes.Length, udpEndpoint);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[DataChannelBridge] UDP zero send error: {e.Message}");
        }
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
        
        if (channelOpen) SendZeroInput();
        udpClient?.Close();
        udpClient?.Dispose();
        Debug.Log("[DataChannelBridge] Stopped");
    }
}