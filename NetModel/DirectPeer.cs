using System;
using System.Net;

namespace NetModel;

internal class DirectPeer(NetKey id, P2PSocket socket, IPEndPoint address) : Peer(id), IDisposable
{
	internal P2PSocket Socket { get; private set; } = socket;
	public IPEndPoint Address { get; } = address;

	public void Dispose() => Socket.Dispose();
}