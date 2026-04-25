using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Ardalis.GuardClauses;

namespace NetModel;

internal partial class MessageQueue
{
	public void ConsumeAck(Peer sender, Acknowledgement ack)
	{
		buffers[sender.Id].AckTracker.ConsumeAck(ack);
	}

	private class AckTracker(int timestamp) {
		private List<Packet> Unacknowledged { get; } = [];

		// TODO: consolidate messages between packets
		private List<Packet> ResendQueue { get; set; } = [];

		// earliest relevant packet is timestamp-32
		private int timestamp = timestamp;
		// if the nth bit is set, the reliable packet with timestamp=timestamp-nth was recieved (1-indexed)
		private uint acknowledged = 0;

		private void AdvanceTimestamp(int newTime)
		{
			if (newTime <= timestamp)
			{
				throw new ArgumentOutOfRangeException(nameof(newTime), "Timestamp must be monotonically increasing");
			}

			int delta = newTime - timestamp;

			for (int i = 0; i < Math.Min(delta, 32); i++)
			{
				// the current (ith) bit will be shifted away
				if (((acknowledged >> i) & 1) < 1)
				{
					// and we didn't get an ack for it
					// so we should always have one corresponding packet (unless we skipped timestamps)
					Packet? packet = Unacknowledged.SingleOrDefault(p => p.Timestamp == timestamp - i);
					if (packet is null) continue;

					packet.Timestamp = -1; // this will be updated when the packet is resent
					Unacknowledged.Remove(packet);
					ResendQueue.Add(packet);
				}
			}

			if (delta > 31) acknowledged = 0;
			else acknowledged <<= delta;
			timestamp = newTime;
		}

		public IEnumerable<Packet> FlushResends(int timestamp)
		{
			AdvanceTimestamp(timestamp);
			(var result, ResendQueue) = (ResendQueue, new());
			return result.Select((p, i) => { p.Timestamp = timestamp + i + 1; return p; });
		}

		// Get confirmation that a packet was recieved
		public void ConsumeAck(Acknowledgement ack)
		{
			int delta = timestamp - ack.Timestamp;
			if (delta < 0) throw new InvalidOperationException("Ack is from the future?");
			if (delta >= 32) return;

			acknowledged |= ack.BitField << delta;
		}

		// Confirm that a packet was recieved
		public void Acknowledge(Packet packet)
		{
			//FIXME: if we recieve a sent packet, after we send a reset packet, how do we know they are the same?
			Unacknowledged.Remove(packet);
		}

		public void AddPending(Packet packet)
		{
			Guard.Against.InvalidInput(packet, nameof(packet), static p => p.IsReliable, "Packet must be reliable");
			int idx = Unacknowledged.BinarySearch(packet);
			if (idx >= 0) throw new InvalidOperationException("Packet already exists in buffer");
			Unacknowledged.Insert(~idx, packet);
			Unacknowledged.Add(packet);
		}

		public bool GenAck(int timestamp, [NotNullWhen(true)] out Acknowledgement? ack)
		{
			AdvanceTimestamp(timestamp);

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
