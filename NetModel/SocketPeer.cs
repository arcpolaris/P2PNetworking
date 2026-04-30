using System;
using System.Net;

namespace NetModel;

/// <summary>
/// A <see cref="Peer"/> backed by a <see cref="UdpPeerSocket"/>
/// </summary>
/// <param name="id"></param>
/// <param name="socket"></param>
/// <param name="endpoint"></param>
internal class SocketPeer(NetKey id, UdpPeerSocket socket, IPEndPoint endpoint) : Peer(id), IDisposable
{
	internal UdpPeerSocket Socket { get; private set; } = socket;
	public IPEndPoint RemoteEP { get; } = endpoint;

	public void Dispose() => Socket.Dispose();
}