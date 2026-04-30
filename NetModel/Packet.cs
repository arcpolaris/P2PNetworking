using System;
using System.Collections.Generic;

namespace NetModel;

internal sealed class Packet : IComparable<Packet>
{
	public int Sequence { get; set; }

	public bool IsReliable { get; set; }

	public List<IMessage> Messages { get; set; } = [];

	public int CompareTo(Packet other)
	{
		int raw = Sequence.CompareTo(other.Sequence);
		if (raw != 0) return raw;

		// reliable packets go BEFORE unreliable ones
		return -IsReliable.CompareTo(other.IsReliable);
	}
}