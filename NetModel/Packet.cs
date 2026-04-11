using System;
using System.Collections.Generic;
using MessagePack;

namespace NetModel;

[MessagePackObject(AllowPrivate = true)]
internal sealed class Packet : IComparable<Packet>
{
	[Key(0)]
	public int Timestamp { get; set; }

	[Key(1)]
	public bool IsReliable { get; set; }

	[Key(2)]
	public List<IMessage> Messages { get; set; } = [];

	public int CompareTo(Packet other)
	{
		int raw = Timestamp.CompareTo(other.Timestamp);
		if (raw != 0) return raw;

		// reliable packets go BEFORE unreliable ones
		return -IsReliable.CompareTo(other.IsReliable);
	}
}
