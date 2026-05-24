using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace NetModel;

internal class MessagePump(Network network)
{
	private Network network = network;
	private ConcurrentDictionary<NetKey, MessageLink> links = [];

	public void Subscribe(SocketPeer peer)
	{
		if (links.TryAdd(peer.Id, MessageLink.StartAround(network, peer))) return;

		throw new ArgumentException("Peer already exists in dictionary");
	}

	public void Remove(SocketPeer peer)
	{
		if (links.TryRemove(peer.Id, out _)) return;

		throw new ArgumentException("Peer was not found in dictionary");
	}

	public void ProcessFrame()
	{
		foreach (MessageLink link in links.Values)
			link.ProcessFrame();
	}

	public void SendFrame()
	{
		foreach (MessageLink link in links.Values)
			link.SendFrame();
	}

	public void SendFrame(SocketPeer peer)
	{
		links[peer.Id].SendFrame();
	}

	public void Trigger<T>(Peer target, T message, bool reliable = false) where T : class, IMessage
	{
		if (target is null) throw new ArgumentNullException(nameof(target));
		if (target is not SocketPeer) throw new ArgumentException("Cannot invoke on indirect remote peer");
		if (!links.TryGetValue(target.Id, out MessageLink link))
			throw new KeyNotFoundException($"Cannot access data for Peer {target.Id}");
		link.AddMessage(message, reliable);
	}

	public void ConsumeAck(Peer sender, Acknowledgement ack)
	{
		links[sender.Id].ConsumeAck(ack);
	}

	public TimeSpan GetTimeSinceLastMessage(Peer peer)
	{
		if (peer is null) throw new ArgumentNullException(nameof(peer));
		if (peer is not SocketPeer) throw new ArgumentException("Cannot invoke on indirect remote peer");
		if (!links.TryGetValue(peer.Id, out MessageLink link)){
			Trace.Fail($"No link for {peer.Id} in [{string.Join(", ", links.Keys)}]");
			throw new KeyNotFoundException($"Cannot access data for Peer {peer.Id}");
		}
		return DateTime.UtcNow - link.LastRecieved;
	}
}