using NetModel;

namespace TestEngine;

[TestClass]
public sealed class MessageQueueTests
{
	[TestMethod]
	public async Task Send_UnreliableMessage_DeliversOverNetworkPair()
	{
		using LoopbackHarness harness = new();

		TestMessage? received = null;
		Peer? sender = null;

		var pair = await harness.CreateNetworkPairAsync(net =>
		{
			net.Register<TestMessage>(100, (from, msg) =>
			{
				sender = from;
				received = msg;
			});
		});

		pair.Host.SendTo(pair.Host.Peers[0], new TestMessage { Text = "hello", Number = 7 });

		await LoopbackHarness.EventuallyAsync(
			condition: () => received is not null,
			pump: harness.Pump);

		Assert.IsNotNull(received);
		Assert.AreEqual("hello", received.Text);
		Assert.AreEqual(7, received.Number);
		Assert.IsNotNull(sender);
		Assert.AreEqual((ushort)0, sender.Id);
	}

	[TestMethod]
	public async Task Send_ReliableMessage_DeliversOverNetworkPair()
	{
		using LoopbackHarness harness = new();

		List<TestMessage> deliveries = [];

		var pair = await harness.CreateNetworkPairAsync(net =>
			net.Register<TestMessage>(100, (_, msg) => deliveries.Add(msg)));

		pair.Host.SendTo(
			pair.Host.Peers[0],
			new TestMessage { Text = "reliable", Number = 1 },
			reliable: true);

		await LoopbackHarness.EventuallyAsync(
			condition: () => deliveries.Count == 1,
			pump: harness.Pump);

		Assert.AreEqual(1, deliveries.Count);
		Assert.AreEqual("reliable", deliveries[0].Text);
		Assert.AreEqual(1, deliveries[0].Number);
	}

	[TestMethod]
	public async Task Send_MultipleMessagesInSameDirection_PreservesAppendOrder()
	{
		using LoopbackHarness harness = new();

		List<string> deliveries = [];

		var pair = await harness.CreateNetworkPairAsync(net =>
			net.Register<OrderedMessage>(101, (_, msg) => deliveries.Add(msg.Label)));

		Peer clientPeer = pair.Host.Peers[0];

		pair.Host.SendTo(clientPeer, new OrderedMessage { Label = "first" });
		pair.Host.SendTo(clientPeer, new OrderedMessage { Label = "second" });
		pair.Host.SendTo(clientPeer, new OrderedMessage { Label = "third" });

		await LoopbackHarness.EventuallyAsync(
			condition: () => deliveries.Count == 3,
			pump: harness.Pump);

		CollectionAssert.AreEqual(
			new[] { "first", "second", "third" },
			deliveries);
	}

	[TestMethod]
	public async Task Send_MixedReliableAndUnreliable_DeliversBothMessages()
	{
		using LoopbackHarness harness = new();

		List<string> deliveries = [];

		var pair = await harness.CreateNetworkPairAsync(net =>
			net.Register<OrderedMessage>(101, (_, msg) => deliveries.Add(msg.Label)));

		Peer clientPeer = pair.Host.Peers[0];

		pair.Host.SendTo(clientPeer, new OrderedMessage { Label = "unreliable" }, reliable: false);
		pair.Host.SendTo(clientPeer, new OrderedMessage { Label = "reliable" }, reliable: true);

		await LoopbackHarness.EventuallyAsync(
			condition: () => deliveries.Count == 2,
			pump: harness.Pump);

		CollectionAssert.AreEquivalent(
			new[] { "unreliable", "reliable" },
			deliveries);
	}

	[TestMethod]
	public async Task Update_WithNoQueuedMessages_DoesNotDeliverPhantomMessagesOrCrash()
	{
		using LoopbackHarness harness = new();

		int deliveries = 0;

		await harness.CreateNetworkPairAsync(net =>
			net.Register<TestMessage>(100, (_, _) => deliveries++));

		for (int i = 0; i < 5; i++)
		{
			harness.Pump();
			await Task.Delay(10);
		}

		Assert.AreEqual(0, deliveries);
	}

	[TestMethod]
	public async Task BidirectionalTraffic_BothNetworksCanSendAcrossSameConnection()
	{
		using LoopbackHarness harness = new();

		List<string> hostSeen = [];
		List<string> clientSeen = [];

		var pair = await harness.CreateNetworkPairAsync(net =>
			net.Register<OrderedMessage>(101, (from, msg) =>
			{
				if (from.Id == 0)
					clientSeen.Add(msg.Label);
				else
					hostSeen.Add(msg.Label);
			}));

		pair.Host.SendTo(pair.Host.Peers[0], new OrderedMessage { Label = "H->C" });
		pair.Client.Send(new OrderedMessage { Label = "C->H" });

		await LoopbackHarness.EventuallyAsync(
			condition: () => hostSeen.Count == 1 && clientSeen.Count == 1,
			pump: harness.Pump);

		CollectionAssert.AreEqual(new[] { "C->H" }, hostSeen);
		CollectionAssert.AreEqual(new[] { "H->C" }, clientSeen);
	}

	[TestMethod]
	public async Task Send_ManyMessages_RoundTripsThroughNetworkTransport()
	{
		using LoopbackHarness harness = new();

		List<string> deliveries = [];

		var pair = await harness.CreateNetworkPairAsync(net =>
			net.Register<OrderedMessage>(101, (_, msg) => deliveries.Add(msg.Label)));

		Peer clientPeer = pair.Host.Peers[0];

		for (int i = 0; i < 6; i++)
		{
			pair.Host.SendTo(clientPeer, new OrderedMessage { Label = $"m{i}" });
		}

		await LoopbackHarness.EventuallyAsync(
			condition: () => deliveries.Count == 6,
			pump: harness.Pump);

		CollectionAssert.AreEqual(
			new[] { "m0", "m1", "m2", "m3", "m4", "m5" },
			deliveries);
	}
}
