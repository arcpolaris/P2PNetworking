using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ObservableCollections;

namespace NetModel;

/// <summary>
/// Manages peers, message routing, and connection lifecycle for one network session.
/// </summary>
public sealed partial class Network : IDisposable
{
	private readonly ObservableList<Peer> peers;
	internal MessageRegistry MessageRegistry { get; init; }
	private MessagePump MessageQueue { get; init; }

	private Dictionary<NetKey, int> pingLookup = new();

	/// <summary>
	/// Gets the round-trip time to a specific peer
	/// </summary>
	/// <param name="peer"></param>
	/// <returns>RTT in milliseconds, or -1 if a pong has not been received yet</returns>
	public int GetPing(Peer peer)
	{
		ThrowIfNotHost();
		return pingLookup.TryGetValue(peer.Id, out int rtt) ? rtt : -1;
	}

	/// <summary>
	/// Gets the round-trip time to the host
	/// </summary>
	/// <returns>RTT in milliseconds, or -1 if a pong has not been received yet</returns>
	public int GetPing()
	{
		ThrowIfNotClient();
		return pingLookup.TryGetValue(0, out int rtt) ? rtt : -1;
	}

	private NetKey h_peerSequence = 0;

	private SocketPeer? c_host = null;

	/// <summary>
	/// 
	/// </summary>
	/// <exception cref="InvalidOperationException"></exception>
	public Peer? Host
	{
		get
		{
			ThrowIfNotClient();
			return c_host;
		}
	}

	/// <!---->
	public bool IsHost { get; private init; }
	/// <!---->
	public bool IsClient => !IsHost;

	/// <summary>
	/// This instance's peer identifier, if assigned
	/// </summary>
	/// <remarks>
	/// Hosts Id is always 0
	/// </remarks>
	public NetKey? MyId { get; private set; }

	/// <summary>
	/// Provides an observable view onto the internal list of connected peers
	/// </summary>
	public IObservableCollection<Peer> Peers { get; private init; }

	private Network(bool isHost, MessageRegistry registry)
	{
		peers = new ObservableList<Peer>();
		Peers = peers;

		MessageRegistry = registry;
		MessageQueue = new(this);

		IsHost = isHost;
		if (IsHost) MyId = 0;
	}

	/// <summary>
	/// Attempts to connect to a remote UDP endpoint via NAT hole-punching
	/// </summary>
	/// <param name="local">A callback that is executed when the local <see cref="IPEndPoint"/> has been discovered via STUN</param>
	/// <param name="remote">An awaitable method that is used to defer the assignment of the remote <see cref="IPEndPoint"/></param>
	/// <param name="punchTimeout">The time in seconds for how long to wait for connection with the remote after <paramref name="remote"/> yields a value</param>
	/// <returns>The <see cref="UdpPeerSocket"/> representing the remote peer</returns>
	/// <exception cref="TimeoutException"></exception>
	private async Task<UdpPeerSocket> Uplink(Action<IPEndpointMapping> local, Task<IPEndPoint> remote, float punchTimeout)
	{
		if (SynchronizationContext.Current is SynchronizationContext syncCtx)
		{
			Trace.WriteLine(syncCtx.GetType().Name, "info");
		}

		UdpPeerSocket socket = new();
		socket.BindRange(10600, 10799);
		IPEndpointMapping mapping = await socket.DiscoverMapping().ConfigureAwait(false);
		local(mapping);
		var remoteEP = await remote.ConfigureAwait(false);
		socket.SetRemote(remoteEP);

		Trace.WriteLine($"{mapping} Uplinking with {remoteEP}...");

		int ack_seq = 0;

		byte[] GetProbe()
		{
			return MessageRegistry.Marshal(new Packet { Sequence = -1, IsReliable = false, Messages = [new Ring(ack_seq + 1)] });
		}

		using CancellationTokenSource cts = new();
		DateTime cancelLastSet = DateTime.UtcNow;
		TimeSpan remainingTime = TimeSpan.FromSeconds(punchTimeout);

		void TempMessageHandler(ArraySegment<byte> data)
		{
			Packet packet = MessageRegistry.Digest(data);

			if (packet is not { Sequence: -1, IsReliable: false, Messages: [Ring ring] })
				cts.Cancel(); // other side is done
			else
			{
				if (ack_seq >= ring.Sequence) return;

				ack_seq = ring.Sequence;

				TimeSpan delta = DateTime.UtcNow - cancelLastSet;
				cancelLastSet = DateTime.UtcNow;
				remainingTime -= delta;
				remainingTime *= 0.8;

				Trace.WriteLine($"{socket.RemoteEndPoint.Port} -> {socket.LocalEndPoint.Port}");
				Trace.Indent();
				Trace.WriteLine($"{ack_seq} | {remainingTime:ss\\.fff} remaining");
				Trace.Unindent();

				if (cts.IsCancellationRequested) return;

				if (remainingTime < TimeSpan.Zero)
					cts.Cancel();
				else
					cts.CancelAfter(remainingTime);
			}
		}

		socket.OnFrameReceived += TempMessageHandler;
		cts.CancelAfter(remainingTime);

		Task polling = socket.StartPolling();
		await socket.HolePunch(GetProbe, cts.Token).ConfigureAwait(false);

		if (ack_seq < 3)
		{
			socket.Dispose();
			throw new TimeoutException("Hole punching killed for exceeding timeout");
		}

		socket.OnFrameReceived -= TempMessageHandler;
		return socket;
	}

	/// <summary>
	/// Uplink with a remote peer, with the local instace acting as the host
	/// </summary>
	/// <param name="local">Called with the public endpoint for the local socket</param>
	/// <param name="remote">Asynchronously provides the public endpoint for the remote socket</param>
	/// <param name="punchTimeout">The time in seconds that hole punching should be attempted before a <see cref="TimeoutException" /> is thrown</param>
	/// <returns>The admitted client</returns>
	/// <exception cref="InvalidOperationException" />
	/// <exception cref="TimeoutException" />
	public async Task<Peer> Admit(Action<IPEndpointMapping> local, Task<IPEndPoint> remote, float punchTimeout = 30f)
	{
		ThrowIfNotHost();

		UdpPeerSocket socket = await Uplink(local, remote, punchTimeout).ConfigureAwait(false);

		SocketPeer peer = new(++h_peerSequence, socket, socket.RemoteEndPoint);
		MessageQueue.Subscribe(peer);
		peers.Add(peer);

		SendTo<SetId>(peer, new(peer.Id), reliable: true);
		SendTo<AddPeers>(peer, new(peers.Except([peer])), reliable: true);
		SendToAllExcept<AddPeers>(peer, new([peer]), reliable: true);

		return peer;
	}

	/// <summary>
	/// Uplink with a remote peer, with the local instace acting as a client
	/// </summary>
	/// <param name="local">Called with the public endpoint for the local socket</param>
	/// <param name="remote">Asynchronously provides the public endpoint for the remote socket</param>
	/// <param name="punchTimeout">The time in seconds that hole punching should be attempted before a <see cref="TimeoutException" /> is thrown</param>
	/// <returns>The admitted client</returns>
	/// <exception cref="InvalidOperationException" />
	/// <exception cref="TimeoutException" />
	public async Task<Peer> Join(Action<IPEndpointMapping> local, Task<IPEndPoint> remote, float punchTimeout = 30f)
	{
		ThrowIfNotClient();

		UdpPeerSocket socket = await Uplink(local, remote, punchTimeout).ConfigureAwait(false);

		SocketPeer peer = new(0, socket, socket.RemoteEndPoint);
		c_host = peer;
		MessageQueue.Subscribe(c_host);
		peers.Add(peer);

		Send<Ping>(new());

		return peer;
	}

	/// <summary>
	/// Handles one frame of incoming and outogoing messages
	/// </summary>
	public void Update()
	{
		MessageQueue.ProcessFrame();
		MessageQueue.SendFrame();

		CheckTimeouts();
	}

	private void CheckTimeouts()
	{
		if (IsClient)
		{
			if (c_host is null) return;
			if (MessageQueue.GetTimeSinceLastMessage(c_host).TotalSeconds > 10)
			{
				Trace.WriteLine("Closing due to timeout");
				Disconnect();
			}
			return;
		}
		
		foreach (var peer in peers.ToList())
		{
			if (MessageQueue.GetTimeSinceLastMessage(peer).TotalSeconds > 10)
			{
				Trace.WriteLine($"Disconnecting due to timeout: {peer.Id}");
				Kick(peer);
			}
		}
	}

	/// <summary>
	/// Communicates to all connected peers - other than <paramref name="peer"/> - to remove <paramref name="peer"/> from their peer lists.
	/// Also removes and closes the connection with <paramref name="peer"/>
	/// </summary>
	/// <remarks>Only valid for a host</remarks>
	/// <exception cref="InvalidOperationException" />
	public void Kick(Peer peer)
	{
		ThrowIfNotHost();
		Send(new RemovePeers(peer), true);
		MessageQueue.SendFrame((SocketPeer)peer);
		CloseSocket((SocketPeer)peer);
	}

	private void CloseSocket(SocketPeer peer)
	{
		MessageQueue.Remove(peer);
		peers.Remove(peer);
		peer.Dispose();
	}

	/// <summary>
	/// Stops messaging to all peers without disposing the instance
	/// </summary>
	public void Disconnect()
	{
		if (IsHost)
		{
			Send<RemovePeers>(new(new Peer(0)), true);
			MessageQueue.SendFrame();
			foreach (Peer peer in peers.ToList())
			{
				CloseSocket((SocketPeer)peer);
			}
		}
		else
		{
			if (c_host is null) return;
			Send<RemovePeers>(new(new Peer((ushort)MyId!)), true);
			MessageQueue.SendFrame();
			CloseSocket(c_host);
		}
	}

	/// <summary>
	/// <list type="table">
	/// <item>
	/// <term>As Host</term>
	/// <description>Sends <paramref name="message"/> to all peers</description>
	/// </item>
	/// <item>
	/// <term>As Client</term>
	/// <description>Sends <paramref name="message"/> to <see cref="Host"/></description>
	/// </item>
	/// </list>
	/// </summary>
	public void Send<T>(T message, bool reliable = false) where T : class, IMessage
	{
		if (IsHost)
		{
			foreach (Peer p in peers)
			{
				MessageQueue.Trigger(p, message, reliable);
			}
		} else if (c_host is not null)
		{
			MessageQueue.Trigger(c_host!, message, reliable);
		} else
		{
			Trace.WriteLine("Send failed; Host is null");
		}
	}

	/// <summary>
	/// Sends <paramref name="message"/> to <paramref name="peer"/>
	/// </summary>
	public void SendTo<T>(Peer peer, T message, bool reliable = false) where T : class, IMessage
	{
		MessageQueue.Trigger(peer!, message, reliable);
	}

	/// <summary>
	/// Sends <paramref name="message"/> to all connected peers other than <paramref name="peer"/>
	/// </summary>
	/// <remarks>Only valid for a host</remarks>
	/// <exception cref="InvalidOperationException" />
	public void SendToAllExcept<T>(Peer peer, T message, bool reliable = false) where T : class, IMessage
	{
		ThrowIfNotHost();
		foreach (Peer p in peers)
		{
			if (p == peer) continue;
			MessageQueue.Trigger(p, message, reliable);
		}
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		peers.OfType<SocketPeer>().Select(p => p.Socket).ToList().ForEach(p => p.Dispose());
		peers.Clear();
	}

	/// <summary>
	/// Gets the working IP Address from <a href="https://api.ipify.org">api.ipify.org</a>
	/// </summary>
	/// <returns>The working public IP Address</returns>
	/// <exception cref="HttpRequestException" />
	public static async Task<IPAddress> GetPublicIP()
	{
		using HttpClient client = new();
		string ip = await client.GetStringAsync("https://api.ipify.org").ConfigureAwait(false);

		return IPAddress.Parse(ip);
	}

	/// <summary>
	/// Gets the working IP Address from the local network interface
	/// </summary>
	/// <returns>The working private IP Address</returns>
	/// <exception cref="InvalidOperationException" />
	public static async Task<IPAddress> GetPrivateIP()
	{
		IPHostEntry host = await Dns.GetHostEntryAsync(Dns.GetHostName());
		IPAddress[] addresses = host.AddressList;
		return addresses.First(ip => ip is { AddressFamily: AddressFamily.InterNetwork } && ip != IPAddress.Any && !IPAddress.IsLoopback(ip));
	}

	/// <exception cref="InvalidOperationException"></exception>
	private void ThrowIfNotHost()
	{
		if (IsHost) return;

		throw new InvalidOperationException("This method is only valid for a host");
	}

	/// <exception cref="InvalidOperationException"></exception>
	private void ThrowIfNotClient()
	{
		if (IsClient) return;

		throw new InvalidOperationException("This method is only valid for a client");
	}
}
