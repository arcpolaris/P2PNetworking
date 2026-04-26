using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ardalis.GuardClauses;

namespace NetModel;

internal partial class MessageQueue
{
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
					info.AckTracker.Acknowledge(packet);
				}
				foreach (IMessage message in packet.Messages)
				{
					InvokeLocal(info.Peer, message);
				}
			}
		}
	}

	public void Subscribe(DirectPeer peer)
	{
		buffers.Add(peer.Id, new(peer));
		peer.Socket.OnMessageRecieved += data => SocketCallback(peer, data);
	}

	public void Remove(DirectPeer peer)
	{
		buffers.Remove(peer.Id);
	}

	private void SocketCallback(Peer peer, ArraySegment<byte> data)
	{
		Packet packet = registry.Digest(data);
		if (packet is null) return;
		buffers[peer.Id].JitterBuffer.Add(packet);

#if DEBUG
		Trace.WriteLine($"Packet {packet.Timestamp} From {peer.Id} | {(packet.IsReliable ? "Reliable" : "Unreliable")}");
		Trace.Indent();
		foreach (var msg in packet.Messages.Select(m => m.GetType().ToString()))
			Trace.WriteLine(msg);
		Trace.Unindent();
#endif
	}

	public void SendFrame()
	{
		foreach (PeerInfo info in buffers.Values)
		{
			info.UpdateTimestamps();

			if (DateTime.UtcNow.Subtract(info.LastPing) >= TimeSpan.FromSeconds(0.1))
			{
				InvokeRemote(info.Peer, new Ping());
				info.LastPing = DateTime.UtcNow;
			}

			if (info.OutboundReliable.Messages.Count > 0)
			{
				byte[] outboundReliable = registry.Marshal(info.OutboundReliable);
				info.Peer.Socket.Send(outboundReliable);

				info.AckTracker.AddPending(info.OutboundReliable);

				info.OutboundReliable = new Packet { IsReliable = true };
			}

			if (info.AckTracker.GenAck(info.Timestamp, out var ack))
			{
				InvokeRemote(info.Peer, ack);
			}

			if (info.Outbound.Messages.Count > 0)
			{
				byte[] outbound = registry.Marshal(info.Outbound);
				info.Peer.Socket.Send(outbound);

				info.Outbound = new Packet() { IsReliable = false };
			}

			foreach (var packet in info.AckTracker.FlushResends(info.Timestamp))
			{
				info.Timestamp++;

				byte[] digest = registry.Marshal(packet);
				info.Peer.Socket.Send(digest);
				info.AckTracker.AddPending(packet);
			}
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