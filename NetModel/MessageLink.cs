using System;
using System.Collections.Concurrent;
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
	private ConcurrentQueue<IMessage> Outbound { get; set; } = [];
	private ConcurrentQueue<IMessage> OutboundReliable { get; set; } = [];

	public SocketPeer Peer { get; init; }

	public DateTime LastPing { get; set; } = DateTime.UnixEpoch;

	public void AddMessage<T>(T message, bool reliably) where T : class, IMessage
	{
		ConcurrentQueue<IMessage> builder = reliably ? OutboundReliable : Outbound;
		builder.Enqueue(message);
	}

	public void ProcessFrame()
	{
		var packets = JitterBuffer.Consume();
		Trace.WriteLine(packets.Count, "jitter consume");
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
			Outbound.Enqueue(new Ping());
			LastPing = DateTime.UtcNow;
		}

		if (OutboundReliable.Count > 0)
		{
			Packet packet = new()
			{
				IsReliable = true,
				Messages = OutboundReliable.ToList()
			};
			OutboundReliable.Clear();

			Send(packet);
		}

		GenAck();

		if (Outbound.Count > 0)
		{
			Packet packet = new()
			{
				IsReliable = false,
				Messages = Outbound.ToList()
			};
			Outbound.Clear();

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

		Trace.WriteLine($"{packet} - {string.Join(' ', digest.Select(b => b.ToString("X2")))}", "sent");
		Packet debug = Parent.MessageRegistry.Digest(digest);
		Trace.Assert(packet.Sequence == debug.Sequence, "Sequences do not match");
		Trace.Assert(packet.IsReliable == debug.IsReliable, "Reliability does not match");
		Trace.Assert(packet.Messages.Select(msg => msg.GetType().FullName).SequenceEqual(debug.Messages.Select(msg => msg.GetType().FullName)));
	}


	private void SocketCallback(ArraySegment<byte> data)
	{
		Trace.WriteLine("recv raw");
		Packet packet = Parent.MessageRegistry.Digest(data);
		Trace.WriteLine(packet, "recv");
		// if Sequence got this high naturally i don't care
		if (packet is null or { Sequence: -1 }) return;

		JitterBuffer.Add(packet);
	}

	private void Dispatch(IMessage message)
	{
		NetKey key = Parent.MessageRegistry.Lookup(message.GetType());
		var rpc = Parent.MessageRegistry.GetRpc(key);
		rpc.Invoke(Parent, Peer, message);
	}
}
