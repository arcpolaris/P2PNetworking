using System;
using System.Collections.Generic;

namespace NetModel;

internal sealed class Packet : IComparable<Packet>
{
	public int Timestamp { get; set; }

	public bool IsReliable { get; set; }

	public List<IMessage> Messages { get; set; } = [];

	public int CompareTo(Packet other)
	{
		int raw = Timestamp.CompareTo(other.Timestamp);
		if (raw != 0) return raw;

		// reliable packets go BEFORE unreliable ones
		return -IsReliable.CompareTo(other.IsReliable);
	}

	//public override string ToString()
	//{
	//	Stringbuild
	//}
}