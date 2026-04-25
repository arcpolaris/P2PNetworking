using System;

namespace NetModel;

internal partial class MessageQueue
{
	private class PeerInfo
	{
		public PeerInfo(DirectPeer peer)
		{
			JitterBuffer = new(peer);
			AckTracker = new(Timestamp);
			Outbound = new()
			{
				IsReliable = false,
			};
			OutboundReliable = new()
			{
				IsReliable = true,
			};

			Peer = peer;
			LastPing = DateTime.UnixEpoch;
		}

		public void UpdateTimestamps()
		{
			Outbound.Timestamp = Timestamp;
			OutboundReliable.Timestamp = Timestamp;
			Timestamp++;
		}

		public JitterBuffer JitterBuffer { get; init; }
		public AckTracker AckTracker { get; init; }
		public Packet Outbound { get; set; }
		public Packet OutboundReliable { get; set; }

		public DirectPeer Peer { get; init; }

		// outbound btw
		public int Timestamp { get; set; } = 0;
		public DateTime LastPing { get; set; }
	}
}