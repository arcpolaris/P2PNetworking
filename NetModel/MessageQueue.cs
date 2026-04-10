using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;

namespace NetModel;

internal class MessageQueue
{
	class PeerInfo
	{
		public PeerInfo(Peer peer)
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
		}

		public JitterBuffer JitterBuffer { get; set; }
		public Packet Outbound { get; set; }
		public Packet OutboundReliable { get; set; }

		public int Timestamp { get; set; }
	}

	private Dictionary<NetKey, PeerInfo> buffers = [];
	private MessageBus messageBus;

	public MessageQueue(MessageBus bus)
	{
		messageBus = bus;
	}

	public void ProcessFrame()
	{
		
	}

	public void Subscribe(Peer peer)
	{
		buffers.Add(peer.Id, new(peer));
		DirectPeer direct = Guard.Against.WrongType<DirectPeer>(peer);
		direct.Socket.OnMessageRecieved += data => SocketCallback(peer, data);
	}

	private void SocketCallback(Peer peer, ArraySegment<byte> data)
	{
		Packet packet = messageBus.Digest(data);
		buffers[peer.Id].JitterBuffer.Add(packet);
	}

	public void SendFrame()
	{

	}

	internal void InvokeRemote<T>(Peer target, NetKey method, T message) where T : class, IMessage
	{
		Guard.Against.WrongType<DirectPeer>(target, message: "Cannot invoke on indirect remote peer");

	}

	internal void InvokeLocal<T>(Peer from, NetKey method, T message) where T : class, IMessage
	{
		
	}
}