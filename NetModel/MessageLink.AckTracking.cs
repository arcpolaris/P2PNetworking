using System;
using System.Collections.Generic;
using System.Linq;

namespace NetModel;

internal partial class MessageLink
{
	List<Packet> pending = [];
	List<Packet> resends = [];

	private int incoming_seq = 0;
	// nth bit set (zero indexed) =>
	// packet with seq=incoming_seq-n has been recv
	private uint received = 0;

	// consume remote's ack of our messages
	public void ConsumeAck(Acknowledgement ack)
	{
		var (sequence, bitfield) = ack;
		for (int i = 0; i < 32; i++)
		{
			if ((bitfield & (1u << i)) == 0) continue;

			// TODO: use binary search here
			var packet = pending.SingleOrDefault(p => p.Sequence == sequence - i);
			if (packet == null) continue;
			pending.Remove(packet);
		}
	}

	private List<Packet> FlushResends()
	{
		while (pending.Count > 0 && sequence - pending[0].Sequence > 8)
		{
			resends.Add(pending[0]);
			pending.RemoveAt(0);
		}
		(var res, resends) = (resends, []);
		return res;
	}

	// hold a packet's information until it's acknowledged
	private void AddPending(Packet packet)
	{
		int idx = pending.BinarySearch(packet);
		if (idx >= 0) throw new ArgumentException("Packet sequence is already present in pending list", nameof(packet));
		pending.Insert(~idx, packet);

		if (pending.Count <= 16) return;

		resends.Add(pending[0]);
		pending.RemoveAt(0);
	}

	// generate an acknowledgement of the remote's messages
	private void GenAck()
	{
		if (received == 0) return;
		Acknowledgement ack = new(incoming_seq, received);
		Outbound.Enqueue(ack);
	}

	private void RecordReceived(Packet packet)
	{
		int seq = packet.Sequence;
		if (seq > incoming_seq)
		{
			int shift = seq - incoming_seq;
			incoming_seq = seq;
			received <<= shift;
		}
		int delta = incoming_seq - seq;
		uint mask = 1u << delta;
		received |= mask;
	}
}
