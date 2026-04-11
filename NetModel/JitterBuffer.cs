using System;
using System.Collections.Generic;
using System.Linq;

namespace NetModel;

// btw we won't be fragmenting
internal class JitterBuffer
{
	private const int capacity = 4;

	// gotta keep ts sorted
	private List<Packet> buffer;

	public Peer Remote { get; init; }

	public JitterBuffer(Peer peer)
	{
		buffer = new();
		Remote = peer;
	}

	/// <summary>
	/// Add a packet to the incoming buffer, then if unreliable packets exceed capacity, drop the oldest
	/// </summary>
	/// <param name="packet"></param>
	public void Add(Packet packet)
	{
		int idx = buffer.BinarySearch(packet);
		if (idx >= 0) throw new InvalidOperationException("Packet already exists in buffer");
		buffer.Insert(~idx, packet);

		if (packet.IsReliable) return;

		if (buffer.Where(p => !p.IsReliable).Count() <= capacity) return;

		// at this point we *should* only be one over cap
		buffer.RemoveAt(buffer.FindIndex(p => !p.IsReliable));
	}

	public List<Packet> Consume()
	{
		var res = buffer.TakeWhile(p => p.IsReliable).ToList();
		buffer.RemoveRange(0, res.Count);
		if (buffer.Count == 0) return res;

		res.Append(buffer[0]);
		buffer.RemoveAt(0);

		return res;
	}
}
