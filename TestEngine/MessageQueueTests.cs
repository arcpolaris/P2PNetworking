// TestEngine/MessageQueueTests.cs
using NetModel;

namespace TestEngine;

[TestClass]
public sealed class MessageQueueTests
{
	[TestMethod]
	public async Task InvokeRemote_UnreliableMessage_DeliversOverLoopback()
	{
		await using LoopbackHarness harness = new();

		Peer? sender = null;
		TestMessage? received = null;

		var queues = harness.CreateConnectedQueues<TestMessage>(
			100,
			onLeft: (_, _) => { },
			onRight: (from, msg) =>
			{
				sender = from;
				received = msg;
			});

		queues.LeftQueue.InvokeRemote(
			queues.LeftRemote,
			new TestMessage { Text = "hello", Number = 7 });

		await LoopbackHarness.EventuallyAsync(
			condition: () => received is not null,
			pump: () =>
			{
				queues.LeftQueue.SendFrame();
				queues.RightQueue.ProcessFrame();
			});

		Assert.IsNotNull(received);
		Assert.AreEqual("hello", received.Text);
		Assert.AreEqual(7, received.Number);
		Assert.IsNotNull(sender);
		Assert.AreEqual(1, sender.Id);
	}

	[TestMethod]
	public async Task InvokeRemote_ReliableMessage_DeliversOverLoopback()
	{
		await using LoopbackHarness harness = new();

		var deliveries = new List<TestMessage>();

		var queues = harness.CreateConnectedQueues<TestMessage>(
			100,
			onLeft: (_, _) => { },
			onRight: (_, msg) => deliveries.Add(msg));

		queues.LeftQueue.InvokeRemote(
			queues.LeftRemote,
			new TestMessage { Text = "reliable", Number = 1 },
			reliable: true);

		await LoopbackHarness.EventuallyAsync(
			condition: () => deliveries.Count == 1,
			pump: () =>
			{
				queues.LeftQueue.SendFrame();
				queues.RightQueue.ProcessFrame();
			});

		Assert.AreEqual(1, deliveries.Count);
		Assert.AreEqual("reliable", deliveries[0].Text);
		Assert.AreEqual(1, deliveries[0].Number);
	}

	[TestMethod]
	public async Task InvokeRemote_MultipleMessagesInSamePacket_PreservesAppendOrder()
	{
		await using LoopbackHarness harness = new();

		var deliveries = new List<string>();

		var queues = harness.CreateConnectedQueues<OrderedMessage>(
			101,
			onLeft: (_, _) => { },
			onRight: (_, msg) => deliveries.Add(msg.Label));

		queues.LeftQueue.InvokeRemote(
			queues.LeftRemote,
			new OrderedMessage { Label = "first" });

		queues.LeftQueue.InvokeRemote(
			queues.LeftRemote,
			new OrderedMessage { Label = "second" });

		queues.LeftQueue.InvokeRemote(
			queues.LeftRemote,
			new OrderedMessage { Label = "third" });

		await LoopbackHarness.EventuallyAsync(
			condition: () => deliveries.Count == 3,
			pump: () =>
			{
				queues.LeftQueue.SendFrame();
				queues.RightQueue.ProcessFrame();
			});

		CollectionAssert.AreEqual(
			new[] { "first", "second", "third" },
			deliveries);
	}

	[TestMethod]
	public async Task InvokeRemote_MixedReliableAndUnreliable_PreservesLogicalOrderAcrossPackets()
	{
		await using LoopbackHarness harness = new();

		var deliveries = new List<string>();

		var queues = harness.CreateConnectedQueues<OrderedMessage>(
			101,
			onLeft: (_, _) => { },
			onRight: (_, msg) => deliveries.Add(msg.Label));

		queues.LeftQueue.InvokeRemote(
			queues.LeftRemote,
			new OrderedMessage { Label = "unreliable" },
			reliable: false);

		queues.LeftQueue.InvokeRemote(
			queues.LeftRemote,
			new OrderedMessage { Label = "reliable" },
			reliable: true);

		await LoopbackHarness.EventuallyAsync(
			condition: () => deliveries.Count == 2,
			pump: () =>
			{
				queues.LeftQueue.SendFrame();
				queues.RightQueue.ProcessFrame();
			});

		CollectionAssert.AreEqual(
			new[] { "reliable", "unreliable" },
			deliveries);
	}

	[TestMethod]
	public async Task SendFrame_WithNoQueuedMessages_StillSendsKeepAlivePacketWithoutCrashing()
	{
		await using LoopbackHarness harness = new();

		int deliveries = 0;

		var queues = harness.CreateConnectedQueues<TestMessage>(
			100,
			onLeft: (_, _) => { },
			onRight: (_, _) => deliveries++);

		for (int i = 0; i < 5; i++)
		{
			queues.LeftQueue.SendFrame();
			queues.RightQueue.ProcessFrame();
			await Task.Delay(10);
		}

		Assert.AreEqual(0, deliveries);
	}

	[TestMethod]
	public async Task BidirectionalTraffic_BothQueuesCanSendOnSameLoopbackPair()
	{
		await using LoopbackHarness harness = new();

		var leftSeen = new List<string>();
		var rightSeen = new List<string>();

		var pair = harness.CreateSocketPair();

		MessageRegistry leftRegistry = new();
		MessageRegistry rightRegistry = new();

		leftRegistry.Register<OrderedMessage>(101, (_, msg) => leftSeen.Add(msg.Label));
		rightRegistry.Register<OrderedMessage>(101, (_, msg) => rightSeen.Add(msg.Label));

		leftRegistry.Freeze();
		rightRegistry.Freeze();

		MessageQueue leftQueue = new(leftRegistry);
		MessageQueue rightQueue = new(rightRegistry);

		DirectPeer leftRemote = new(2, pair.Left, pair.Left.RemoteEndPoint);
		DirectPeer rightRemote = new(1, pair.Right, pair.Right.RemoteEndPoint);

		leftQueue.Subscribe(leftRemote);
		rightQueue.Subscribe(rightRemote);

		leftQueue.InvokeRemote(leftRemote, new OrderedMessage { Label = "L->R" });
		rightQueue.InvokeRemote(rightRemote, new OrderedMessage { Label = "R->L" });

		await LoopbackHarness.EventuallyAsync(
			condition: () => leftSeen.Count == 1 && rightSeen.Count == 1,
			pump: () =>
			{
				leftQueue.SendFrame();
				rightQueue.SendFrame();
				leftQueue.ProcessFrame();
				rightQueue.ProcessFrame();
			});

		CollectionAssert.AreEqual(new[] { "R->L" }, leftSeen);
		CollectionAssert.AreEqual(new[] { "L->R" }, rightSeen);
	}

	[TestMethod]
	public async Task FormatterPath_PacketWithSeveralMessages_RoundTripsThroughLoopback()
	{
		await using LoopbackHarness harness = new();

		var deliveries = new List<string>();

		var queues = harness.CreateConnectedQueues<OrderedMessage>(
			101,
			onLeft: (_, _) => { },
			onRight: (_, msg) => deliveries.Add(msg.Label));

		for (int i = 0; i < 6; i++)
		{
			queues.LeftQueue.InvokeRemote(
				queues.LeftRemote,
				new OrderedMessage { Label = $"m{i}" });
		}

		await LoopbackHarness.EventuallyAsync(
			condition: () => deliveries.Count == 6,
			pump: () =>
			{
				queues.LeftQueue.SendFrame();
				queues.RightQueue.ProcessFrame();
			});

		CollectionAssert.AreEqual(
			new[] { "m0", "m1", "m2", "m3", "m4", "m5" },
			deliveries);
	}
}