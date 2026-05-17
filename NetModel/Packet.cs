using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

	public override string ToString()
	{
		StringBuilder sb = new();
		sb.Append($"{nameof(Packet)} #{Sequence}");
		if (IsReliable)
			sb.Append($" [Reliable]");
		sb.AppendLine();
		sb.AppendJoin('\n', Messages.Select(static msg => $"\t{msg.GetType()}"));
		return sb.ToString();
	}
}