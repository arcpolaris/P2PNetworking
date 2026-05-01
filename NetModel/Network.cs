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
			}).Register<Acknowledgement>(5, MessageQueue.ConsumeAck);
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

		public static void InitializeHost()
		{
			ThrowIfAlreadyInitialized();
			Instance = ConstructHost();
		}

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

		private static async Task<UdpPeerSocket> Uplink(Action<IPEndPoint> local, Task<IPEndPoint> remote, float punchTimeout)
		{
			UdpPeerSocket socket = new();
			socket.BindAny();
			IPEndPoint stun = await socket.STUN();
			local(stun);
			var remoteEP = await remote;
			socket.SetRemote(remoteEP);

		Debug.WriteLine($"[{socket.LocalEndPoint}]/[{stun}] Uplinking with {remoteEP}...");

		bool got_msg = false; // our hole is punched
		bool got_ack = false;

		//if either peer gets an ack, we're good
		//FIXME: we should really use some other than skipping deserialization with a nil (like an actual message)
		//IDEA: use a message that keeps track of the highest ack sequence, and ramp down timeout as we get more seqs
		byte[] msg = [0xC0,.."MESSAGE"u8];
		byte[] ack = [0xC0,.."ACKNLGE"u8];

			byte[] GetProbe() => got_msg ? ack : msg;

			using CancellationTokenSource cts = new();

			void TempMessageHandler(ArraySegment<byte> data)
			{
				Debug.WriteLine($"{socket.RemoteEndPoint.Port} -> {socket.LocalEndPoint.Port} : {Encoding.UTF8.GetString(data)} {(got_msg ? "X" : "")}");
				if (data.AsSpan().SequenceEqual(msg)) got_msg = true;
				else if (data.AsSpan().SequenceEqual(ack) && !got_ack)
				{
					got_msg = true;
					got_ack = true;
					//their hole is punched, so we don't need to keep punching
					cts.CancelAfter(500);
				}
			}

			socket.OnFrameReceived += TempMessageHandler;
			cts.CancelAfter((int)(1000 * punchTimeout));

			_ = socket.StartPolling();
			await socket.HolePunch(GetProbe, cts.Token);

			if (!got_msg)
			{
				socket.Dispose();
				throw new TimeoutException("Hole punching killed for exceeding timeout");
			}

			socket.OnFrameReceived -= TempMessageHandler;
			return socket;
		}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="local"></param>
	/// <param name="remote"></param>
	/// <param name="punchTimeout"></param>
	/// <returns></returns>
	public async Task<Peer> Admit(Action<IPEndPoint> local, Task<IPEndPoint> remote, float punchTimeout = 30f)
	{
		ThrowIfNotHost();

			UdpPeerSocket socket = await Uplink(local, remote, punchTimeout);

			SocketPeer peer = new(++h_peerSequence, socket, socket.RemoteEndPoint);
			peers.Add(peer);
			MessageQueue.Subscribe(peer);

			SendTo<SetId>(peer, new(peer.Id), reliable: true);
			SendTo<AddPeers>(peer, new(peers.Except([peer])), reliable: true);
			SendToAllExcept<AddPeers>(peer, new([peer]), reliable: true);

			return peer;
		}


		public async Task<Peer> Join(Action<IPEndPoint> local, Task<IPEndPoint> remote, float punchTimeout = 30f)
		{
			ThrowIfNotClient();

			UdpPeerSocket socket = await Uplink(local, remote, punchTimeout);

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
			} else
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

		public void Send<T>(T message, bool reliable = false) where T : class, IMessage
			=> Send(new(IsHost ? TargetKind.AllClients : TargetKind.Host, null), message, reliable);

		public void SendTo<T>(Peer peer, T message, bool reliable = false) where T : class, IMessage
			=> Send(TargetKind.Client, peer, message, reliable);

		public void SendToAllExcept<T>(Peer peer, T message, bool reliable = false) where T : class, IMessage
			=> Send(TargetKind.AllClientsExcept, peer, message, reliable);

		public void Send<T>(TargetKind targetKind, Peer peer, T message, bool reliable = false) where T : class, IMessage
			=> Send(new(targetKind, peer), message, reliable);

		public void Send<T>(in SendTarget target, T message, bool reliable = false) where T : class, IMessage
		{
			switch (target.Kind)
			{
				case TargetKind.Host:
					ThrowIfNotClient();
					MessageQueue.Trigger(c_host!, message, reliable);
					break;
				case TargetKind.Client:
					ThrowIfNotHost();
					MessageQueue.Trigger(target.Peer!, message, reliable);
					break;
				case TargetKind.AllClients:
					ThrowIfNotHost();
					foreach (Peer peer in peers)
					{
						MessageQueue.Trigger(peer, message, reliable);
					}
					break;
				case TargetKind.AllClientsExcept:
					ThrowIfNotHost();
					foreach (Peer peer in peers)
					{
						if (peer == target.Peer) continue;
						MessageQueue.Trigger(peer, message, reliable);
					}
					break;
			}
		}

	/// <inheritdoc/>
	public void Dispose()
	{
		peers.OfType<SocketPeer>().Select(p => p.Socket).ToList().ForEach(p => p.Dispose());
		peers.Clear();

			Instance = null;
		}

		public static async Task<IPAddress> GetPublicIP()
		{
			using HttpClient client = new();
			string ip = await client.GetStringAsync("https://api.ipify.org");

			return IPAddress.Parse(ip);
		}

		private void ThrowIfNotHost()
		{
			if (IsHost) return;

			throw new InvalidOperationException("This method is only valid for a host");
		}

		private void ThrowIfNotClient()
		{
			if (IsClient) return;
		
			throw new InvalidOperationException("This method is only valid for a client");
		}
	}
}