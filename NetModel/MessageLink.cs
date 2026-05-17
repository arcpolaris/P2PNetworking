using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NetModel;

internal partial class MessageLink
{
	private MessageLink(Network parent, SocketPeer peer)
	{
		Parent = parent;
		Peer = peer;
	}

	public static MessageLink StartAround(Network parent, SocketPeer peer)
	{
		MessageLink link = new(parent, peer);
		peer.Socket.OnFrameReceived += link.SocketCallback;
		return link;
	}

	private int sequence = 1;

	private Network Parent { get; }

	private JitterBuffer JitterBuffer { get; } = new();
	private List<IMessage> Outbound { get; set; } = [];
	private List<IMessage> OutboundReliable { get; set; } = [];

	public SocketPeer Peer { get; init; }

	public DateTime LastPing { get; set; } = DateTime.UnixEpoch;

	public void AddMessage<T>(T message, bool reliably) where T : class, IMessage
	{
		List<IMessage> builder = reliably ? OutboundReliable : Outbound;
		builder.Add(message);
	}

	public void ProcessFrame()
	{
		var packets = JitterBuffer.Consume();
		foreach (Packet packet in packets)
		{
			if (packet.IsReliable)
			{
				RecordReceived(packet);
			}
			foreach (IMessage message in packet.Messages)
			{
				Dispatch(message);
			}
		}
	}

	public void SendFrame()
	{
		if (DateTime.UtcNow.Subtract(LastPing) >= TimeSpan.FromSeconds(2))
		{
			Outbound.Add(new Ping());
			LastPing = DateTime.UtcNow;
		}

		if (OutboundReliable.Count > 0)
		{
			Packet packet = new()
			{
				IsReliable = true,
				Messages = OutboundReliable
			};
			OutboundReliable = [];

			Send(packet);
		}

		GenAck();

		if (Outbound.Count > 0)
		{
			Packet packet = new()
			{
				IsReliable = false,
				Messages = Outbound
			};
			Outbound = [];

			Send(packet);
		}

		foreach (var packet in FlushResends())
		{
			Send(packet);
		}
		resends.Clear();
	}

	private void Send(Packet packet)
	{
		packet.Sequence = sequence;
		byte[] digest = Parent.MessageRegistry.Marshal(packet);
		Peer.Socket.Send(digest);

		sequence++;

		if (packet.IsReliable)
		{
			AddPending(packet);
		}
	}


	private void SocketCallback(ArraySegment<byte> data)
	{
		Packet packet = Parent.MessageRegistry.Digest(data);
		// if Sequence got this high naturally i don't care
		if (packet is null or { Sequence: -1 }) return;

		Trace.WriteLine($"Packet {packet.Sequence} From {Peer.Id} | {(packet.IsReliable ? "Reliable" : "Unreliable")}", "packet");
		Trace.Indent();
		foreach (var msg in packet.Messages.Select(m => m.GetType().ToString()))
			Trace.WriteLine(msg, "packet");

		Trace.Unindent();

		JitterBuffer.Add(packet);
	}

	private void Dispatch(IMessage message)
	{
		NetKey key = Parent.MessageRegistry.Lookup(message.GetType());
		var rpc = Parent.MessageRegistry.GetRpc(key);
		rpc.Invoke(Parent, Peer, message);
	}
}
