using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace NetModel
{
	internal partial class MessagePump
	{
		public void ConsumeAck(Peer sender, Acknowledgement ack)
		{
			infos[sender.Id].AckTracker.ConsumeAck(ack);
		}

		private class AckTracker(int sequence)
		{
			private List<Packet> Unacknowledged { get; } = [];

			// TODO: consolidate messages between packets
			private List<Packet> ResendQueue { get; set; } = [];

			// earliest relevant packet is sequence-32
			private int sequence = sequence;
			// if the nth bit is set, the reliable packet with seq=seq-nth was recieved (1-indexed)
			private uint acknowledged = 0;

			private void AdvanceSequence(int newSequence)
			{
				if (newSequence < sequence)
				{
					throw new ArgumentOutOfRangeException(nameof(newSequence), "Sequence must not decrease");
				}

				int delta = newSequence - sequence;

				for (int i = 0; i < Math.Min(delta, 32); i++)
				{
					// the current (ith) bit will be shifted away
					if (((acknowledged >> i) & 1) < 1)
					{
						// and we didn't get an ack for it
						// so we should always have one corresponding packet (unless we skipped a seqid)
						Packet? packet = Unacknowledged.SingleOrDefault(p => p.Sequence == sequence - i);
						if (packet is null) continue;

						packet.Sequence = -1; // this will be updated when the packet is resent
						Unacknowledged.Remove(packet);
						ResendQueue.Add(packet);
					}
				}

				if (delta > 31) acknowledged = 0;
				else acknowledged <<= delta;
				sequence = newSequence;
			}

			public IEnumerable<Packet> FlushResends(int sequence)
			{
				AdvanceSequence(sequence);
				(var result, ResendQueue) = (ResendQueue, new());
				return result.Select((p, i) => { p.Sequence = sequence + i + 1; return p; });
			}

			// Get confirmation that a packet was recieved
			public void ConsumeAck(Acknowledgement ack)
			{
				int delta = sequence - ack.Sequence;
				if (delta < 0) throw new InvalidOperationException("Ack is from the future?");
				if (delta >= 32) return;

				acknowledged |= ack.BitField << delta;
			}

			// Confirm that a packet was recieved
			public void Acknowledge(Packet packet)
			{
				//FIXME: if we recieve a sent packet, after we send a resend packet, how do we know they are the same?
				Unacknowledged.Remove(packet);
			}

			public void AddPending(Packet packet)
			{
				if (!packet.IsReliable) throw new ArgumentException("Pending packet must be reliable", nameof(packet));
				int idx = Unacknowledged.BinarySearch(packet);
				if (idx >= 0) throw new InvalidOperationException("Packet already exists in buffer");
				Unacknowledged.Insert(~idx, packet);
			}

			public bool GenAck(int sequence, [NotNullWhen(true)] out Acknowledgement? ack)
			{
				AdvanceSequence(sequence);

				if (acknowledged == 0)
				{
					ack = null;
					return false;
				}

				ack = new Acknowledgement(acknowledged);
				return true;
			}
		}
	}
}
