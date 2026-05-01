using System;

namespace NetModel;

internal partial class MessagePump
{
	private class PeerInfo
	{
		public PeerInfo(SocketPeer peer)
		{
			JitterBuffer = new(peer);
			AckTracker = new(Sequence);
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

			public void AdvanceSequence()
			{
				Outbound.Sequence = Sequence;
				OutboundReliable.Sequence = Sequence;
				Sequence++;
			}

			public JitterBuffer JitterBuffer { get; init; }
			public AckTracker AckTracker { get; init; }
			public Packet Outbound { get; set; }
			public Packet OutboundReliable { get; set; }

			public SocketPeer Peer { get; init; }

			// outbound btw
			public int Sequence { get; set; } = 0;
			public DateTime LastPing { get; set; }
		}
	}
}