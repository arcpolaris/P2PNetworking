using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NetModel;

// btw we won't be fragmenting
internal class JitterBuffer
{
	private const int capacity = 8;

	// gotta keep ts sorted
	private ConcurrentQueue<Packet> queue;
	private List<Packet> buffer;

	public Peer Remote { get; init; }

	public JitterBuffer(Peer peer)
	{
		buffer = new();
		queue = new();
		Remote = peer;
	}

	public void Add(Packet packet) => queue.Enqueue(packet);

	private void Tap(Packet packet)
	{
		int idx = buffer.BinarySearch(packet);
		if (idx >= 0) throw new InvalidOperationException("Packet already exists in buffer");
		buffer.Insert(~idx, packet);

		if (packet.IsReliable) return;

		if (buffer.Where(static p => !p.IsReliable).Count() <= capacity) return;

		// at this point we *should* only be one over cap
		int drop = buffer.FindIndex(static p => !p.IsReliable);
		Debug.WriteLine(buffer[drop]);
		buffer.RemoveAt(drop);
	}

	private void Drain()
	{
		while (queue.TryDequeue(out Packet packet))
		{
			Tap(packet);
		}
	}

	public List<Packet> Consume()
	{
		Drain();

		var res = buffer.TakeWhile(p => p.IsReliable).ToList();
		buffer.RemoveRange(0, res.Count);
		if (buffer.Count == 0) return res;

		res.Add(buffer[0]);
		buffer.RemoveAt(0);

		return res;
	}
}
