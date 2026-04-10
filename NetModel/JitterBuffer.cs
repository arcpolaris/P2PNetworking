using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ardalis.GuardClauses;

namespace NetModel;

// btw we won't be fragmenting
internal class JitterBuffer
{
	private const int capacity = 4;

	private int outgoingTimestamp = 0;

	private List<Packet> incoming;

	public Peer Remote { get; init; }

	public JitterBuffer(Peer peer)
	{
		incoming = new();
		Remote = peer;
	}

	public void Add(Packet incoming)
	{

	}

	/// <summary>
	/// Consume the packet with the lowest timestamp and execute its calls
	/// </summary>
	/// <returns>The timestamp of the packet</returns>
	public int? Consume()
	{
		if (incoming.Count == 0) return null;

	}
}
