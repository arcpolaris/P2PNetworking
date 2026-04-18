using NetModel;

namespace TestEngine;

[TestClass]
public sealed class MarshalingTests
{
	[TestMethod]
	public void MessageRegistry_MarshalDigest_RoundTripsMixedPacket()
	{
		MessageRegistry registry = new();

		registry
			.Register<TestMessage>(100, (_, _) => { })
			.Register<OrderedMessage>(101, (_, _) => { })
			.Register<SetId>(102, (_, _) => { })
			.Register<AddPeers>(103, (_, _) => { });

		registry.Freeze();

		Packet outbound = new()
		{
			Timestamp = 42,
			IsReliable = true,
			Messages =
			[
				new TestMessage { Text = "alpha", Number = 7 },
				new OrderedMessage { Label = "beta" },
				new SetId(9),
				new AddPeers([new Peer(1), new Peer(2)])
			]
		};

		byte[] bytes = registry.Marshal(outbound);
		Packet inbound = registry.Digest(bytes);

		Assert.AreEqual(42, inbound.Timestamp);
		Assert.IsTrue(inbound.IsReliable);
		Assert.AreEqual(4, inbound.Messages.Count);

		Assert.IsInstanceOfType<TestMessage>(inbound.Messages[0]);
		Assert.AreEqual("alpha", ((TestMessage)inbound.Messages[0]).Text);
		Assert.AreEqual(7, ((TestMessage)inbound.Messages[0]).Number);

		Assert.IsInstanceOfType<OrderedMessage>(inbound.Messages[1]);
		Assert.AreEqual("beta", ((OrderedMessage)inbound.Messages[1]).Label);

		Assert.IsInstanceOfType<SetId>(inbound.Messages[2]);
		Assert.AreEqual((ushort)9, ((SetId)inbound.Messages[2]).Id);

		Assert.IsInstanceOfType<AddPeers>(inbound.Messages[3]);
		CollectionAssert.AreEqual(
			new ushort[] { 1, 2 },
			((AddPeers)inbound.Messages[3]).Peers.Select(p => p.Id).ToArray());
	}

	[TestMethod]
	public void PacketCompareTo_ReliableComesBeforeUnreliableAtSameTimestamp()
	{
		Packet reliable = new()
		{
			Timestamp = 5,
			IsReliable = true
		};

		Packet unreliable = new()
		{
			Timestamp = 5,
			IsReliable = false
		};

		Assert.IsTrue(reliable.CompareTo(unreliable) < 0);
		Assert.IsTrue(unreliable.CompareTo(reliable) > 0);
	}
}