using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ObservableCollections;

namespace NetModel;

/// <summary>
/// Manages peers, message routing, and connection lifecycle for one network session.
/// </summary>
public sealed class Network : IDisposable
{
	/// <summary>
	/// Provides the global <see cref="Network"/> singleton
	/// </summary>
	public static Network? Instance { get; private set; }

	private readonly ObservableList<Peer> peers;
	private MessageRegistry MessageRegistry { get; init; }
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
	public NotifyCollectionChangedSynchronizedViewList<Peer> Peers { get; private init; }

	private Network()
	{
		peers = new ObservableList<Peer>();
		Peers = peers.CreateView(static p => p).ToNotifyCollectionChanged();

		MessageRegistry = new();
		MessageQueue = new(MessageRegistry);

		MessageRegistry
		.Register<Ping>(0, (sender, ping) =>
		{
			if (IsHost) SendTo<Pong>(sender, new(ping));
			else Send<Pong>(new(ping));
		})
		.Register<Pong>(1, (sender, pong) =>
		{
			pingLookup[sender.Id] = (int)pong.Delta.TotalMilliseconds;
		})
		.Register<AddPeers>(2, (sender, addPeers) =>
		{
			ThrowIfNotClient();
			peers.AddRange(addPeers.Peers);
		})
		.Register<RemovePeers>(3, (sender, removePeers) =>
		{
			//FIXME
			ThrowIfNotClient();

			if (removePeers.Peers.Any(p => p.Id == MyId || p.Id == 0)) CloseSocket(c_host!);
			else
			{
				foreach (Peer peer in removePeers.Peers)
				{
					peers.Remove(peer);
				}
			}
		}).Register<SetId>(4, (sender, setId) =>
		{
			ThrowIfNotClient();

			MyId = setId.Id;
		}).Register<Acknowledgement>(5, MessageQueue.ConsumeAck)
		.Register<Ring>(6, (_,_) => { });
	}

	private static void ThrowIfAlreadyInitialized()
	{
		if (Instance is not null)
		{
			throw new InvalidOperationException("Network singleton is already initialized");
		}
	}

	internal static Network ConstructHost()
	{
		return new Network()
		{
			IsHost = true,
			MyId = 0
		};
	}

	internal static Network ConstructClient()
	{
		return new Network()
		{
			IsHost = false,
		};
	}

	//TODO: have message registration be done at construction time

	/// <summary>
	/// Constructs <see cref="Instance"/> as a host
	/// </summary>
	public static void InitializeHost()
	{
		ThrowIfAlreadyInitialized();
		Instance = ConstructHost();
	}

	/// <summary>
	/// Constructs <see cref="Instance"/> as a client
	/// </summary>
	public static void InitializeClient()
	{
		ThrowIfAlreadyInitialized();
		Instance = ConstructClient();
	}

	/// <summary>
	/// Freezes the internal message registry to optimize lookups
	/// </summary>
	/// <remarks>
	/// Subsequent calls to <see cref="Network.Register{T}(ushort, MessageHandler{T})"/> will fail
	/// </remarks>
	public void FinishSetup()
	{
		MessageRegistry.Freeze();
	}

	/// <summary>
	/// Attempts to connect to a remote UDP endpoint via NAT hole-punching
	/// </summary>
	/// <param name="local">A callback that is executed when the local <see cref="IPEndPoint"/> has been discovered via STUN</param>
	/// <param name="remote">An awaitable method that is used to defer the assignment of the remote <see cref="IPEndPoint"/></param>
	/// <param name="punchTimeout">The time in seconds for how long to wait for connection with the remote after <paramref name="remote"/> yields a value</param>
	/// <returns>The <see cref="UdpPeerSocket"/> representing the remote peer</returns>
	/// <exception cref="TimeoutException"></exception>
	private async Task<UdpPeerSocket> Uplink(Action<IPEndPoint> local, Task<IPEndPoint> remote, float punchTimeout)
	{
		if (SynchronizationContext.Current is SynchronizationContext syncCtx)
		{
			Trace.WriteLine(syncCtx.GetType().Name, "info");
		}

		UdpPeerSocket socket = new();
		socket.BindRange(10600, 10799);
		IPEndPoint stun = await socket.STUN().ConfigureAwait(false);
		local(stun);
		var remoteEP = await remote.ConfigureAwait(false);
		socket.SetRemote(remoteEP);

		Trace.WriteLine($"[{socket.LocalEndPoint}]/[{stun}] Uplinking with {remoteEP}...");

		int ack_seq = 0;

		//TODO: have the timer halve the current value

		byte[] GetProbe()
		{
			return MessageRegistry.Marshal(new Packet { Sequence = -1, IsReliable = false, Messages = [new Ring(ack_seq + 1)] });
		}

		using CancellationTokenSource cts = new();

		void TempMessageHandler(ArraySegment<byte> data)
		{
			Trace.WriteLine($"{socket.RemoteEndPoint.Port} -> {socket.LocalEndPoint.Port} : {data.Count}/[{string.Join(" ", data.Select(b => b.ToString("X2")))}] {ack_seq}");
			Packet packet = MessageRegistry.Digest(data);

			if (packet is not { Sequence: -1, IsReliable: false, Messages: [Ring ring] })
				cts.Cancel(); // other side is done
			else
			{
				ack_seq = ring.Sequence;

				double remaining = punchTimeout / Math.Pow(2, ack_seq);
				Trace.WriteLine(remaining);
				if (remaining > 0.5)
					cts.CancelAfter(TimeSpan.FromSeconds(remaining));
			}
		}

		socket.OnFrameReceived += TempMessageHandler;
		cts.CancelAfter(TimeSpan.FromSeconds(punchTimeout));

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
	public async Task<Peer> Admit(Action<IPEndPoint> local, Task<IPEndPoint> remote, float punchTimeout = 30f)
	{
		ThrowIfNotHost();

		UdpPeerSocket socket = await Uplink(local, remote, punchTimeout).ConfigureAwait(false);

		SocketPeer peer = new(++h_peerSequence, socket, socket.RemoteEndPoint);
		peers.Add(peer);
		MessageQueue.Subscribe(peer);

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
	public async Task<Peer> Join(Action<IPEndPoint> local, Task<IPEndPoint> remote, float punchTimeout = 30f)
	{
		ThrowIfNotClient();

		UdpPeerSocket socket = await Uplink(local, remote, punchTimeout).ConfigureAwait(false);

		SocketPeer peer = new(0, socket, socket.RemoteEndPoint);
		peers.Add(peer);
		c_host = peer;
		MessageQueue.Subscribe(c_host);

		Send<Ping>(new());

		return peer;
	}

	public void Update()
	{
		MessageQueue.ProcessFrame();
		MessageQueue.SendFrame();
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
		//WARNING: we don't tell someone when they have been kicked
		SendToAllExcept(peer, new RemovePeers(peer), true);
		CloseSocket((SocketPeer)peer);
	}

	private void CloseSocket(SocketPeer peer)
	{
		MessageQueue.Remove(peer);
		peers.Remove(peer);
		peer.Dispose();
	}

	public void Disconnect()
	{
		if (IsHost)
		{
			foreach (Peer peer in peers.ToList())
			{
				CloseSocket((SocketPeer)peer);
			}
			Send<RemovePeers>(new(new Peer(0)), true);
		}
		else
		{
			if (c_host is null) return;
			Send<RemovePeers>(new(new Peer((ushort)MyId!)), true);
			CloseSocket(c_host);
		}
	}

	public void Register<T>(NetKey key, MessageHandler<T> rpc) where T : class, IMessage
	{
		MessageRegistry.Register<T>(key, rpc);
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
		} else
		{
			MessageQueue.Trigger(c_host!, message, reliable);
		}
	}

	/// <summary>
	/// Sends <paramref name="message"/> to <paramref name="peer"/>
	/// </summary>
	/// <remarks>Only valid for a host</remarks>
	/// <exception cref="InvalidOperationException" />
	public void SendTo<T>(Peer peer, T message, bool reliable = false) where T : class, IMessage
	{
		ThrowIfNotHost();
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

		Instance = null;
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
