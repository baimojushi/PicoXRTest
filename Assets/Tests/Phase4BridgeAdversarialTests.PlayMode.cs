using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GalleryRuntime;

/// <summary>Actual UDP socket assertions, NOT a claim of browser or video end-to-end coverage.</summary>
public sealed class Phase4BridgeAdversarialTests
{
    private GameObject host;
    private DataChannelUdpBridge bridge;
    private UdpClient udp;
    private int port;

    [SetUp]
    public void Setup()
    {
        udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        udp.Client.ReceiveTimeout = 250;
        port = ((IPEndPoint)udp.Client.LocalEndPoint).Port;
        host = new GameObject("Adversarial UDP Bridge");
        host.SetActive(false);
        bridge = host.AddComponent<DataChannelUdpBridge>();
        bridge.udpHost = "127.0.0.1";
        bridge.udpPort = port;
        bridge.sendRateHz = 40;
        bridge.disconnectTimeoutSec = 0.2f;
        host.SetActive(true);
    }

    [TearDown]
    public void TearDown()
    {
        if (host != null) UnityEngine.Object.Destroy(host);
        udp?.Close();
    }

    string ReadMatching(Func<RemoteInputPacket, bool> predicate, float seconds = 2f)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                string raw = Encoding.UTF8.GetString(udp.Receive(ref sender));
                var packet = JsonUtility.FromJson<RemoteInputPacket>(raw);
                if (packet != null && predicate(packet)) return raw;
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode != SocketError.TimedOut) throw;
            }
        }
        Assert.Fail("No matching UDP packet received within deadline");
        return null;
    }

    [UnityTest]
    public IEnumerator ControlMapsToActualRemoteInputPacket()
    {
        bridge.OnChannelOpened();
        bridge.EnqueueBytes(Encoding.UTF8.GetBytes("{\"type\":\"control\",\"lx\":0.5,\"ly\":1,\"rx\":-0.25,\"ry\":0.1,\"use\":true,\"reset\":false}"));
        yield return new WaitForSecondsRealtime(0.1f);
        string raw = ReadMatching(p => p.moveY > 0.9f);
        var packet = JsonUtility.FromJson<RemoteInputPacket>(raw);
        Assert.That(packet.moveX, Is.EqualTo(0.5f).Within(0.01f));
        Assert.That(packet.lookX, Is.EqualTo(-0.25f).Within(0.01f));
        Assert.IsTrue(packet.interact);
        Assert.Greater(packet.seq, 0);
    }

    [UnityTest]
    public IEnumerator PingCannotKeepStaleMovementAlive()
    {
        bridge.OnChannelOpened();
        bridge.EnqueueBytes(Encoding.UTF8.GetBytes("{\"type\":\"control\",\"ly\":1}"));
        yield return new WaitForSecondsRealtime(0.08f);
        ReadMatching(p => p.moveY > 0.9f);
        for (int i = 0; i < 5; ++i)
        {
            bridge.EnqueueBytes(Encoding.UTF8.GetBytes("{\"type\":\"ping\"}"));
            yield return new WaitForSecondsRealtime(0.075f);
        }
        ReadMatching(p => p.moveY == 0 && p.moveX == 0);
    }

    [UnityTest]
    public IEnumerator CloseClearsOldQueueAndInput()
    {
        bridge.OnChannelOpened();
        bridge.EnqueueBytes(Encoding.UTF8.GetBytes("{\"type\":\"control\",\"ly\":1}"));
        yield return new WaitForSecondsRealtime(0.08f);
        ReadMatching(p => p.moveY > 0.9f);
        bridge.OnChannelClosed();
        bridge.EnqueueBytes(Encoding.UTF8.GetBytes("{\"type\":\"control\",\"ly\":1}"));
        yield return new WaitForSecondsRealtime(0.07f);
        ReadMatching(p => p.moveY == 0);
    }
}
