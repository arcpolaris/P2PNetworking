using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;

namespace NetModel;

internal class MessageQueue
{
	private class PeerInfo
	{
		public PeerInfo(DirectPeer peer)
		{
			JitterBuffer = new(peer);
			Outbound = new()
			{
				IsReliable = false,
			};
			OutboundReliable = new()
			{
				IsReliable = true,
			};

			Peer = peer;
		}

		public void UpdateTimestamps()
		{
			Outbound.Timestamp = Timestamp;
			OutboundReliable.Timestamp = Timestamp;
			Timestamp++;
		}

		public JitterBuffer JitterBuffer { get; init; }
		public Packet Outbound { get; set; }
		public Packet OutboundReliable { get; set; }

		public DirectPeer Peer { get; init; }

		// outbound btw
		public int Timestamp { get; set; }
	}

	private Dictionary<NetKey, PeerInfo> buffers = [];
	private MessageRegistry registry;

	public MessageQueue(MessageRegistry registry)
	{
		this.registry = registry;
	}

	public void ProcessFrame()
	{
		foreach (PeerInfo info in buffers.Values)
		{
			var packets = info.JitterBuffer.Consume();
			foreach (Packet packet in packets)
			{
				// TODO: Implement reliable packets, for now just do testing without them
				//if (packet.IsReliable)
				//{
				//	throw new NotImplementedException();
				//}
				foreach (IMessage message in packet.Messages)
				{
					InvokeLocal(info.Peer, message);
				}
			}
		}
	}

	public void Subscribe(Peer peer)
	{
		DirectPeer direct = Guard.Against.WrongType<DirectPeer>(peer);
		buffers.Add(peer.Id, new(direct));
		direct.Socket.OnMessageRecieved += data => SocketCallback(peer, data);
	}

	public void Unsubscribe(Peer peer)
	{
		if (!buffers.ContainsKey(peer.Id)) return;
		DirectPeer direct = Guard.Against.WrongType<DirectPeer>(peer);
		direct.Socket.OnMessageRecieved -= data => SocketCallback(peer, data);
		buffers.Remove(peer.Id);
	}

	private void SocketCallback(Peer peer, ArraySegment<byte> data)
	{
		Packet packet = registry.Digest(data);
		buffers[peer.Id].JitterBuffer.Add(packet);
	}

	public void SendFrame()
	{
		foreach (PeerInfo info in buffers.Values)
		{
			info.UpdateTimestamps();

			// even if there's nothing to send, unreliable is our keep-alive packet
			byte[] outbound = registry.Marshal(info.Outbound);
			info.Peer.Socket.Send(outbound);

			if (info.OutboundReliable.Messages.Count > 0)
			{
				byte[] outboundReliable = registry.Marshal(info.OutboundReliable);
				info.Peer.Socket.Send(outboundReliable);

				info.OutboundReliable = new Packet { IsReliable = true };
			}

			info.Outbound = new Packet() { IsReliable = false };
		}
	}

	internal void InvokeRemote<T>(Peer target, T message, bool reliable = false) where T : class, IMessage
	{
		Guard.Against.WrongType<DirectPeer>(target, message: "Cannot invoke on indirect remote peer");
		PeerInfo info = buffers[target.Id];
		Packet outbound = reliable ? info.OutboundReliable : info.Outbound;
		outbound.Messages.Add(message);
	}

	private void InvokeLocal(Peer from, IMessage message)
	{
		NetKey key = registry.Lookup(message.GetType());
		var rpc = registry.GetRpc(key);
		rpc.Invoke(from, message);
	}
}