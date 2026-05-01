using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NetModel;

/// <summary>
/// Buffers <see cref="Packet"/> instances received from a remote peer and releases them in sequence
/// </summary>
/// <remarks>
/// Reliable packets are held until they are the next packets at the front of the
/// sorted buffer. Unreliable packets are capped to a small buffer window and the
/// oldest unreliable packet is dropped when that window is exceeded
/// </remarks>
internal class JitterBuffer
{
	private const int capacity = 8;

	// gotta keep ts sorted
	private ConcurrentQueue<Packet> queue;
	private List<Packet> buffer;

	public JitterBuffer()
	{
		buffer = new();
		queue = new();
	}

	/// <summary>
	/// Queues a <see cref="Packet"/> to be inserted into the sorted jitter buffer on the next consume pass
	/// </summary>
	/// <remarks>
	/// This method is thread safe
	/// </remarks>
	/// <seealso cref="Consume"/>
	public void Add(Packet packet) => queue.Enqueue(packet);

	/// <summary>
	/// Inserts a <see cref="Packet"/> into its sorted position in the buffer
	/// </summary>
	/// <param name="packet"></param>
	/// <exception cref="InvalidOperationException"></exception>
	/// <seealso cref="Packet.CompareTo(Packet)"/>
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

	/// <summary>
	/// Dequeues all pertinent packets
	/// </summary>
	/// <returns>
	/// All reliable packets and the oldest unreliable packet by sequence
	/// </returns>
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
