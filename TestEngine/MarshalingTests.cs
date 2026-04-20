using NetModel;

namespace TestEngine;

public sealed class MarshalingTests
{
	[Test]
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

		Assert.That.AreEqual(42, inbound.Timestamp);
		Assert.That.IsTrue(inbound.IsReliable);
		Assert.That.AreEqual(4, inbound.Messages.Count);

		Assert.That.AreEqual("alpha", ((TestMessage)inbound.Messages[0]).Text);
		Assert.That.AreEqual(7, ((TestMessage)inbound.Messages[0]).Number);

		Assert.That.AreEqual("beta", ((OrderedMessage)inbound.Messages[1]).Label);

		Assert.That.AreEqual((ushort)9, ((SetId)inbound.Messages[2]).Id);

		Assert.That.SequenceEqual(
			new ushort[] { 1, 2 },
			((AddPeers)inbound.Messages[3]).Peers.Select(p => p.Id).ToArray());
	}

	[Test]
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

		Assert.That.IsTrue(reliable.CompareTo(unreliable) < 0);
		Assert.That.IsTrue(unreliable.CompareTo(reliable) > 0);
	}
}