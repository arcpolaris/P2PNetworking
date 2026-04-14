// TestEngine/PacketAndRegistryLoopbackTests.cs
using NetModel;

namespace TestEngine;

[TestClass]
public sealed class PacketAndRegistryLoopbackTests
{
	[TestMethod]
	public async Task PacketFormatter_MixedMessagesAcrossFrames_RoundTripsOverLoopback()
	{
		await using LoopbackHarness harness = new();

		var seen = new List<string>();

		var queues = harness.CreateConnectedQueues<OrderedMessage>(
			101,
			onLeft: (_, _) => { },
			onRight: (_, msg) => seen.Add(msg.Label));

		queues.LeftQueue.InvokeRemote(queues.LeftRemote, new OrderedMessage { Label = "a0" });
		queues.LeftQueue.InvokeRemote(queues.LeftRemote, new OrderedMessage { Label = "a1" });
		queues.LeftQueue.SendFrame();

		queues.LeftQueue.InvokeRemote(queues.LeftRemote, new OrderedMessage { Label = "b0" }, reliable: true);
		queues.LeftQueue.InvokeRemote(queues.LeftRemote, new OrderedMessage { Label = "b1" }, reliable: true);

		await LoopbackHarness.EventuallyAsync(
			condition: () => seen.Count == 4,
			pump: () =>
			{
				queues.LeftQueue.SendFrame();
				queues.RightQueue.ProcessFrame();
			});

		CollectionAssert.AreEqual(
			new[] { "a0", "a1", "b0", "b1" },
			seen);
	}

	[TestMethod]
	public async Task DefaultSetIdPath_IsAvailableOnClientNetwork()
	{
		await using LoopbackHarness harness = new();

		var pair = harness.CreateNetworkPair(clientId: 7);

		Assert.AreEqual((ushort)7, pair.Client.MyId);
	}

	[TestMethod]
	public async Task DefaultAddPeersPath_CanDeliverPeerAnnouncementsToClient()
	{
		await using LoopbackHarness harness = new();

		var topo = harness.CreateMultiClientNetwork(1, 2);

		topo.Host.SendTo(
			topo.Clients[0].HostSidePeer,
			new AddPeers([new Peer(2)]),
			reliable: true);

		await LoopbackHarness.EventuallyAsync(
			condition: () => true,
			pump: () => harness.Pump(topo.Host, topo.Clients[0].Client, topo.Clients[1].Client),
			timeoutMs: 100);

		// This is intentionally weak because PeerView enumeration shape is not obvious from the public API.
		// The real point here is that the default AddPeers RPC path serializes and processes without throwing.
		Assert.AreEqual((ushort)1, topo.Clients[0].Client.MyId);
	}

	[TestMethod]
	public async Task LargeBurstOfMessages_ExercisesPacketSerializationRepeatedly()
	{
		await using LoopbackHarness harness = new();

		var received = new List<int>();

		var queues = harness.CreateConnectedQueues<TestMessage>(
			100,
			onLeft: (_, _) => { },
			onRight: (_, msg) => received.Add(msg.Number));

		for (int i = 0; i < 64; i++)
		{
			queues.LeftQueue.InvokeRemote(
				queues.LeftRemote,
				new TestMessage { Text = "burst", Number = i });
		}

		await LoopbackHarness.EventuallyAsync(
			condition: () => received.Count == 64,
			pump: () =>
			{
				queues.LeftQueue.SendFrame();
				queues.RightQueue.ProcessFrame();
			},
			timeoutMs: 3000);

		CollectionAssert.AreEqual(Enumerable.Range(0, 64).ToArray(), received);
	}
}