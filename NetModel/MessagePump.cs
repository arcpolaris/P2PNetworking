using System;
using System.Collections.Generic;

namespace NetModel;

internal class MessagePump(Network network)
{
	private Network network = network;
	private Dictionary<NetKey, MessageLink> links = [];

	public void Subscribe(SocketPeer peer)
	{
		links.Add(peer.Id, MessageLink.StartAround(network, peer));
	}

	public void Remove(SocketPeer peer)
	{
		links.Remove(peer.Id);
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
}