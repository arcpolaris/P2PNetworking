using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;

namespace NetModel;

[MessagePackObject(AllowPrivate = true)]
internal partial class Ping : IMessage
{
	[Key(0)]
	public DateTime Time { get; init; }

	[SerializationConstructor]
	private Ping(DateTime time) => Time = time;

	public Ping() => Time = DateTime.UtcNow;
}

[MessagePackObject(AllowPrivate = true)]
internal partial class Pong : IMessage
{
	[Key(0)]
	public TimeSpan Delta { get; init; }

	[SerializationConstructor]
	private Pong(TimeSpan delta) => Delta = delta;

	public Pong(Ping ping) => Delta = ping.Time - DateTime.UtcNow;
}

[MessagePackObject(AllowPrivate = true)]
internal partial class AddPeers : IMessage
{
	[Key(0)]
	public List<Peer> Peers { get; init; }

	[SerializationConstructor]
	public AddPeers(List<Peer> peers) => Peers = peers;

	public AddPeers(IEnumerable<Peer> peers) : this(peers.ToList()) { }
}

[MessagePackObject(AllowPrivate = true)]
internal partial class SetId : IMessage
{
	[Key(0)]
	public NetKey Id { get; init; }

	[SerializationConstructor]
	public SetId(NetKey id) => Id = id;
}