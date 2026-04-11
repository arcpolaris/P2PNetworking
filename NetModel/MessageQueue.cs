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
				Timestamp = 0,
			};
			OutboundReliable = new()
			{
				IsReliable = true,
				Timestamp = 0,
			};

			Peer = peer;
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
				if (packet.IsReliable)
				{
					throw new NotImplementedException();
				}
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

	private void SocketCallback(Peer peer, ArraySegment<byte> data)
	{
		Packet packet = registry.Digest(data);
		buffers[peer.Id].JitterBuffer.Add(packet);
	}

	public void SendFrame()
	{
		foreach (PeerInfo info in buffers.Values)
		{
			byte[] outbound = registry.Marshal(info.Outbound);
			byte[] outboundReliable = registry.Marshal(info.OutboundReliable);

			info.Peer.Socket.Send(outbound);
			info.Peer.Socket.Send(outboundReliable);
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