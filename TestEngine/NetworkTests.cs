// TestEngine/NetworkTests.cs
using NetModel;

namespace TestEngine;

[TestClass]
public sealed class NetworkTests
{
	[TestMethod]
	public async Task HostSendTo_ClientReceivesOverLoopback()
	{
		await using LoopbackHarness harness = new();

		TestMessage? received = null;
		Peer? sender = null;

		var pair = harness.CreateNetworkPair(
			clientId: 1,
			configureClient: client =>
			{
				client.Register<TestMessage>(100, (from, msg) =>
				{
					sender = from;
					received = msg;
				});
			});

		pair.Host.SendTo(
			pair.HostSidePeer,
			new TestMessage { Text = "host->client", Number = 10 });

		await LoopbackHarness.EventuallyAsync(
			condition: () => received is not null,
			pump: () => harness.Pump(pair.Host, pair.Client));

		Assert.IsNotNull(received);
		Assert.AreEqual("host->client", received.Text);
		Assert.AreEqual(10, received.Number);
		Assert.IsNotNull(sender);
		Assert.AreEqual(0, sender.Id);
	}

	[TestMethod]
	public async Task ClientSend_HostReceivesOverLoopback()
	{
		await using LoopbackHarness harness = new();

		TestMessage? received = null;
		Peer? sender = null;

		var pair = harness.CreateNetworkPair(
			clientId: 1,
			configureHost: host =>
			{
				host.Register<TestMessage>(100, (from, msg) =>
				{
					sender = from;
					received = msg;
				});
			});

		pair.Client.Send(new TestMessage { Text = "client->host", Number = 11 });

		await LoopbackHarness.EventuallyAsync(
			condition: () => received is not null,
			pump: () => harness.Pump(pair.Host, pair.Client));

		Assert.IsNotNull(received);
		Assert.AreEqual("client->host", received.Text);
		Assert.AreEqual(11, received.Number);
		Assert.IsNotNull(sender);
		Assert.AreEqual(1, sender.Id);
	}

	[TestMethod]
	public async Task HostBroadcast_AllClientsReceiveOverLoopback()
	{
		await using LoopbackHarness harness = new();

		var seenByClient1 = new List<string>();
		var seenByClient2 = new List<string>();

		var topo = harness.CreateMultiClientNetwork(1, 2);

		topo.Clients[0].Client.Register<OrderedMessage>(101, (_, msg) => seenByClient1.Add(msg.Label));
		topo.Clients[1].Client.Register<OrderedMessage>(101, (_, msg) => seenByClient2.Add(msg.Label));

		topo.Host.FinishSetup();
		topo.Clients[0].Client.FinishSetup();
		topo.Clients[1].Client.FinishSetup();

		topo.Host.Send(new OrderedMessage { Label = "broadcast" });

		await LoopbackHarness.EventuallyAsync(
			condition: () => seenByClient1.Count == 1 && seenByClient2.Count == 1,
			pump: () =>
			{
				harness.Pump(topo.Host, topo.Clients[0].Client, topo.Clients[1].Client);
			});

		CollectionAssert.AreEqual(new[] { "broadcast" }, seenByClient1);
		CollectionAssert.AreEqual(new[] { "broadcast" }, seenByClient2);
	}

	[TestMethod]
	public async Task HostSendToAllExcept_ExcludedClientDoesNotReceive()
	{
		await using LoopbackHarness harness = new();

		var seenByClient1 = new List<string>();
		var seenByClient2 = new List<string>();

		var topo = harness.CreateMultiClientNetwork(1, 2);

		topo.Clients[0].Client.Register<OrderedMessage>(101, (_, msg) => seenByClient1.Add(msg.Label));
		topo.Clients[1].Client.Register<OrderedMessage>(101, (_, msg) => seenByClient2.Add(msg.Label));

		topo.Host.FinishSetup();
		topo.Clients[0].Client.FinishSetup();
		topo.Clients[1].Client.FinishSetup();

		DirectPeer excludedPeer = topo.Clients[0].HostSidePeer;

		topo.Host.SendToAllExcept(
			excludedPeer,
			new OrderedMessage { Label = "everyone-but-1" });

		await LoopbackHarness.EventuallyAsync(
			condition: () => seenByClient2.Count == 1,
			pump: () =>
			{
				harness.Pump(topo.Host, topo.Clients[0].Client, topo.Clients[1].Client);
			});

		Assert.AreEqual(0, seenByClient1.Count);
		CollectionAssert.AreEqual(new[] { "everyone-but-1" }, seenByClient2);
	}

	[TestMethod]
	public async Task ClientPing_TriggersDefaultPong_AndUpdatesHostRtt()
	{
		await using LoopbackHarness harness = new();

		var pair = harness.CreateNetworkPair(clientId: 1);

		pair.Client.Send(new Ping(), reliable: false);

		await LoopbackHarness.EventuallyAsync(
			condition: () =>
			{
				try
				{
					_ = pair.Host.RTT(pair.HostSidePeer);
					return true;
				}
				catch
				{
					return false;
				}
			},
			pump: () => harness.Pump(pair.Host, pair.Client),
			timeoutMs: 3000);

		uint rtt = pair.Host.RTT(pair.HostSidePeer);
		Assert.IsTrue(rtt <= 5_000);
	}

	[TestMethod]
	public void Close_ClientWithNoHost_IsSafeNoOp()
	{
		Network client = Network.ConstructClient();
		client.Close();
	}

	[TestMethod]
	public void Close_HostWithNoPeers_IsSafeNoOp()
	{
		Network host = Network.ConstructHost();
		host.Close();
	}

	[TestMethod]
	public void HostProperty_OnClient_ReturnsConfiguredHostPeer()
	{
		using var socket = new P2PSocket();
		socket.BindAny();
		socket.SetRemote(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, socket.LocalEndPoint.Port));

		Network client = Network.ConstructClient();

		var hostPeer = new DirectPeer(0, socket, socket.RemoteEndPoint);

		typeof(Network)
			.GetField("c_host", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
			.SetValue(client, hostPeer);

		Assert.AreSame(hostPeer, client.Host);
	}
}