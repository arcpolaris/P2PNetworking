using System.Linq;
using NetModel;

namespace TestEngine;

[TestClass]
public sealed class A_NetworkTests
{
	[TestMethod, Priority(0)]
	public async Task TryJoin_AssignsClientId_AndExposesHostPeer()
	{
		using LoopbackHarness harness = new();

		var pair = await harness.CreateNetworkPairAsync(configure: null);

		Assert.IsNotNull(pair.Client.Host);
		Assert.AreEqual((ushort)0, pair.Client.Host!.Id);
		Assert.AreEqual((ushort)0, pair.Host.MyId);
		Assert.AreEqual(1, pair.Host.Peers.Count);
		Assert.AreEqual(pair.Client.MyId, pair.Host.Peers[0].Id);
	}

	[TestMethod]
	public async Task HostSend_ClientReceivesOverLoopback()
	{
		using LoopbackHarness harness = new();

		TestMessage? received = null;
		Peer? sender = null;

		var pair = await harness.CreateNetworkPairAsync(
			net =>
			{
				if (!net.IsHost)
				{
					net.Register<TestMessage>(100, (from, msg) =>
					{
						sender = from;
						received = msg;
					});
				}
			});

		pair.Host.Send(new TestMessage
		{
			Text = "host->client",
			Number = 10
		});

		await LoopbackHarness.EventuallyAsync(
			condition: () => received is not null,
			pump: harness.Pump);

		Assert.IsNotNull(received);
		Assert.IsNotNull(sender);
		Assert.AreEqual("host->client", received.Text);
		Assert.AreEqual(10, received.Number);
		Assert.AreEqual((ushort)0, sender.Id);
	}

	[TestMethod]
	public async Task ClientSend_HostReceivesOverLoopback()
	{
		using LoopbackHarness harness = new();

		TestMessage? received = null;
		Peer? sender = null;

		var pair = await harness.CreateNetworkPairAsync(
			net =>
			{
				if (net.IsHost)
				{
					net.Register<TestMessage>(100, (from, msg) =>
					{
						sender = from;
						received = msg;
					});
				}
			});

		pair.Client.Send(new TestMessage
		{
			Text = "client->host",
			Number = 11
		});

		await LoopbackHarness.EventuallyAsync(
			condition: () => received is not null,
			pump: harness.Pump);

		Assert.IsNotNull(received);
		Assert.IsNotNull(sender);
		Assert.AreEqual("client->host", received.Text);
		Assert.AreEqual(11, received.Number);
		Assert.AreEqual(pair.Client.MyId, sender.Id);
	}

	[TestMethod]
	public async Task HostBroadcast_AllClientsReceiveOverLoopback()
	{
		using LoopbackHarness harness = new();

		List<string>[] seenByClient =
		[
			[],
			[]
		];

		int configuredClients = 0;
		var topo = await harness.CreateMultiClientNetworkAsync(
			2,
			net =>
			{
				if (net.IsHost)
					return;

				int clientIndex = configuredClients++;
				net.Register<OrderedMessage>(101, (_, msg) => seenByClient[clientIndex].Add(msg.Label));
			});

		topo.Host.Send(new OrderedMessage { Label = "broadcast" });

		await LoopbackHarness.EventuallyAsync(
			condition: () => seenByClient[0].Count == 1 && seenByClient[1].Count == 1,
			pump: harness.Pump);

		CollectionAssert.AreEqual(new[] { "broadcast" }, seenByClient[0]);
		CollectionAssert.AreEqual(new[] { "broadcast" }, seenByClient[1]);
		Assert.AreEqual((ushort)1, topo.Clients[0].MyId);
		Assert.AreEqual((ushort)2, topo.Clients[1].MyId);
	}

	[TestMethod]
	public async Task HostSendToAllExcept_ExcludedClientDoesNotReceive()
	{
		using LoopbackHarness harness = new();

		List<string>[] seenByClient =
		[
			[],
			[]
		];

		int configuredClients = 0;
		var topo = await harness.CreateMultiClientNetworkAsync(
			2,
			net =>
			{
				if (net.IsHost)
					return;

				int clientIndex = configuredClients++;
				net.Register<OrderedMessage>(101, (_, msg) => seenByClient[clientIndex].Add(msg.Label));
			});

		Peer excludedPeer = topo.Host.Peers.Single(p => p.Id == topo.Clients[0].MyId);
		topo.Host.SendToAllExcept(excludedPeer, new OrderedMessage { Label = "everyone-but-1" });

		await LoopbackHarness.EventuallyAsync(
			condition: () => seenByClient[1].Count == 1,
			pump: harness.Pump);

		Assert.AreEqual(0, seenByClient[0].Count);
		CollectionAssert.AreEqual(new[] { "everyone-but-1" }, seenByClient[1]);
	}

	[TestMethod]
	public async Task TryJoin_DefaultPingPong_UpdatesClientRtt()
	{
		using LoopbackHarness harness = new();

		var pair = await harness.CreateNetworkPairAsync(configure: null);

		await LoopbackHarness.EventuallyAsync(
			condition: () =>
			{
				try
				{
					_ = pair.Client.RTT(pair.Client.Host!);
					return true;
				}
				catch (KeyNotFoundException)
				{
					return false;
				}
			},
			pump: harness.Pump,
			timeoutMs: 3000);

		uint rtt = pair.Client.RTT(pair.Client.Host!);
		Assert.IsTrue(rtt <= 5_000);
	}

	[TestMethod]
	public async Task Kick_RemovesOnlyTargetedClientFromFurtherDelivery()
	{
		using LoopbackHarness harness = new();

		List<string>[] seenByClient =
		[
			[],
			[]
		];

		int configuredClients = 0;
		var topo = await harness.CreateMultiClientNetworkAsync(
			2,
			net =>
			{
				if (net.IsHost)
					return;

				int clientIndex = configuredClients++;
				net.Register<OrderedMessage>(101, (_, msg) => seenByClient[clientIndex].Add(msg.Label));
			});

		Peer removedPeer = topo.Host.Peers.Single(p => p.Id == topo.Clients[0].MyId);
		topo.Host.Kick(removedPeer);

		await LoopbackHarness.EventuallyAsync(
			condition: () => topo.Host.Peers.Count == 1 && topo.Clients[1].Peers.Count == 1,
			pump: harness.Pump,
			timeoutMs: 1000);

		topo.Host.Send(new OrderedMessage { Label = "after-kick" });

		await LoopbackHarness.EventuallyAsync(
			condition: () => seenByClient[1].Count == 1,
			pump: harness.Pump);

		Assert.AreEqual(0, seenByClient[0].Count);
		CollectionAssert.AreEqual(new[] { "after-kick" }, seenByClient[1]);
	}

	[TestMethod]
	public void Disconnect_ClientWithNoHost_IsSafeNoOp()
	{
		using Network client = Network.ConstructClient();
		client.FinishSetup();
		client.Disconnect();
	}

	[TestMethod]
	public void Disconnect_HostWithNoPeers_IsSafeNoOp()
	{
		using Network host = Network.ConstructHost();
		host.FinishSetup();
		host.Disconnect();
	}
}
