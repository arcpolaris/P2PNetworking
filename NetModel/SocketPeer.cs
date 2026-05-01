using System;
using System.Net;

namespace NetModel;

/// <summary>
/// A <see cref="Peer"/> backed by a <see cref="UdpPeerSocket"/>
/// </summary>
internal class SocketPeer(NetKey id, UdpPeerSocket socket, IPEndPoint endpoint) : Peer(id), IDisposable
{
	internal UdpPeerSocket Socket { get; private set; } = socket;
	public IPEndPoint RemoteEP { get; } = endpoint;

	/// <inheritdoc/>
	public void Dispose() => Socket.Dispose();
}