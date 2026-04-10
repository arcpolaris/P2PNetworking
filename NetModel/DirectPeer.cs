using System.Net;

namespace NetModel;

internal class DirectPeer(NetKey id, P2PSocket socket, IPEndPoint address) : Peer(id)
{
	internal P2PSocket Socket { get; private set; } = socket;
	public IPEndPoint Address { get; } = address;
}