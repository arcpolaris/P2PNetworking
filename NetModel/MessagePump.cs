using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NetModel
{
	internal partial class MessagePump(MessageRegistry registry)
	{
		private Dictionary<NetKey, PeerInfo> infos = [];
		private MessageRegistry registry = registry;

		public void ProcessFrame()
		{
			foreach (PeerInfo info in infos.Values)
			{
				var packets = info.JitterBuffer.Consume();
				foreach (Packet packet in packets)
				{
					if (packet.IsReliable)
					{
						info.AckTracker.Acknowledge(packet);
					}
					foreach (IMessage message in packet.Messages)
					{
						Dispatch(info.Peer, message);
					}
				}
			}
		}

		public void Subscribe(SocketPeer peer)
		{
			infos.Add(peer.Id, new(peer));
			peer.Socket.OnFrameReceived += data => SocketCallback(peer, data);
		}

		public void Remove(SocketPeer peer)
		{
			infos.Remove(peer.Id);
		}

		private void SocketCallback(Peer peer, ArraySegment<byte> data)
		{
			Packet packet = registry.Digest(data);
			if (packet is null) return;
			infos[peer.Id].JitterBuffer.Add(packet);

#if DEBUG
			Trace.WriteLine($"Packet {packet.Sequence} From {peer.Id} | {(packet.IsReliable ? "Reliable" : "Unreliable")}");
			Trace.Indent();
			foreach (var msg in packet.Messages.Select(m => m.GetType().ToString()))
				Trace.WriteLine(msg);
			Trace.Unindent();
#endif
		}

		public void SendFrame()
		{
			foreach (PeerInfo info in infos.Values)
			{
				info.AdvanceSequence();

				if (DateTime.UtcNow.Subtract(info.LastPing) >= TimeSpan.FromSeconds(0.1))
				{
					Trigger(info.Peer, new Ping());
					info.LastPing = DateTime.UtcNow;
				}

				if (info.OutboundReliable.Messages.Count > 0)
				{
					byte[] outboundReliable = registry.Marshal(info.OutboundReliable);
					info.Peer.Socket.Send(outboundReliable);

					info.AckTracker.AddPending(info.OutboundReliable);

					info.OutboundReliable = new Packet { IsReliable = true };
				}

				if (info.AckTracker.GenAck(info.Sequence, out var ack))
				{
					Trigger(info.Peer, ack);
				}

				if (info.Outbound.Messages.Count > 0)
				{
					byte[] outbound = registry.Marshal(info.Outbound);
					info.Peer.Socket.Send(outbound);

					info.Outbound = new Packet() { IsReliable = false };
				}

				foreach (var packet in info.AckTracker.FlushResends(info.Sequence))
				{
					info.Sequence++;

					byte[] digest = registry.Marshal(packet);
					info.Peer.Socket.Send(digest);
					info.AckTracker.AddPending(packet);
				}
			}
		}

		internal void Trigger<T>(Peer target, T message, bool reliable = false) where T : class, IMessage
		{
			if (target is not SocketPeer) throw new ArgumentException("Cannot invoke on indirect remote peer");
			PeerInfo info = infos[target.Id];
			Packet outbound = reliable ? info.OutboundReliable : info.Outbound;
			outbound.Messages.Add(message);
		}

		private void Dispatch(Peer from, IMessage message)
		{
			NetKey key = registry.Lookup(message.GetType());
			var rpc = registry.GetRpc(key);
			rpc.Invoke(from, message);
		}
	}
}