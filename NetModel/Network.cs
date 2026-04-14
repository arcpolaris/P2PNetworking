using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentResults;
using Ardalis.GuardClauses;
using System.Net;
using System.Threading;
using System.Linq;
using ObservableCollections;

namespace NetModel;

public sealed class Network
{
	public static Network? Instance { get; private set; }

	private ObservableList<Peer> Peers { get; init; }
	private MessageRegistry MessageRegistry { get; init; }
	private MessageQueue MessageQueue { get; init; }

	private Dictionary<NetKey, uint> pingRTT = new();

	public uint RTT(Peer peer) => pingRTT[peer.Id];

	private NetKey h_peer_counter = 0;

	private DirectPeer? c_host = null;
	public Peer? Host
	{
		get
		{
			Guard.Against.NotClient(this);
			return c_host;
		}
	}

	public bool IsHost { get; private init; }

	// host will always have Id 0
	public NetKey? MyId { get; private set; }

	public ISynchronizedView<Peer, NetKey> PeerView { get; private init; }

	private Network()
	{
		Peers = new ObservableList<Peer>();
		PeerView = Peers.CreateView(p => p.Id);		

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
			pingRTT[sender.Id] = (uint)pong.Delta.TotalMilliseconds;
		})
		.Register<AddPeers>(2, (sender, addPeers) =>
		{
			Guard.Against.NotClient(this);
			Peers.AddRange(addPeers.Peers);
		})
		.Register<RemovePeers>(3, (sender, removePeers) =>
		{
			Guard.Against.NotClient(this);

			if (removePeers.Peers.Any(p => p.Id == MyId)) Close();
			else
			{
				foreach (Peer peer in removePeers.Peers)
				{
					Peers.Remove(peer);
				}
			}
		}).Register<SetId>(4, (sender, setId) =>
		{
			Guard.Against.NotClient(this);

			MyId = setId.Id;
		});
	}

	private static void ThrowIfAlreadyInitialized()
	{
		if (Instance is not null)
		{
			throw new InvalidOperationException("Network singleton is already initialized");
		}
	}

	private static Network ConstructHost()
	{
		return new Network()
		{
			IsHost = true,
			MyId = 0
		};
	}

	private static Network ConstructClient()
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

	public static void InitalizeClient()
	{
		ThrowIfAlreadyInitialized();
		Instance = ConstructClient();
	}

#if DEBUG
	public static Network __NewHost() => ConstructHost();
	public static Network __NewClient() => ConstructClient();
#warning Instance constructors should only be used for testing
#endif

	public void FinishSetup()
	{
		MessageRegistry.Freeze();
	}

	private static async Task<Result<P2PSocket>> TryUplink(Action<Task<IPEndPoint>> local, Func<Task<IPEndPoint>> remote, float punchTimeout)
	{
		P2PSocket socket = new();
		socket.BindAny();
		local(socket.STUN());
		socket.SetRemote(await remote());

		bool got_msg = false; // our hole is punched

		//if either peer gets an ack, we're good

		//random garbage
		byte[] msg = [0x0f, 0x27, 0xdc, 0x1b, 0xa1, 0x3c, 0x6c, 0xa5];
		byte[] ack = [0x36, 0xe5, 0xc7, 0xd5, 0xa6, 0x43, 0xc4, 0xff];

		byte[] GetProbe() => got_msg ? ack : msg;

		using CancellationTokenSource cts = new();

		void TempMessageHandler(ArraySegment<byte> data)
		{
			if (msg.AsSpan().SequenceEqual(data)) got_msg = true;
			else if (msg.AsSpan().SequenceEqual(ack))
			{
				//their hole is punched, so we don't need to keep punching
				cts.CancelAfter(2000);
			}
		}

		socket.OnMessageRecieved += TempMessageHandler;
		cts.CancelAfter((int)(1000 * punchTimeout));

		_ = socket.StartPolling();
		await socket.HolePunch(GetProbe, cts.Token);

		if (!got_msg)
		{
			socket.Dispose();
			return Result.Fail<P2PSocket>("Local socket was not punched into");
		}

		socket.OnMessageRecieved -= TempMessageHandler;
		return socket;
	}

	// VERY NOT THREAD SAFE
	public async Task<Result<Peer>> TryAdmit(Action<Task<IPEndPoint>> local, Func<Task<IPEndPoint>> remote, float punchTimeout = 30f)
	{
		Guard.Against.NotHost(this);

		Result<P2PSocket> uplinkResult = await TryUplink(local, remote, punchTimeout);

		if (uplinkResult.IsFailed) return Result.Fail(uplinkResult.Errors);

		P2PSocket socket = uplinkResult.Value;
		DirectPeer peer = new(++h_peer_counter, socket, socket.RemoteEndPoint);
		Peers.Add(peer);
		MessageQueue.Subscribe(peer);

		SendTo<SetId>(peer, new(peer.Id), reliable: true);
		SendTo<AddPeers>(peer, new(Peers.Except([peer])), reliable: true);
		SendToAllExcept<AddPeers>(peer, new([peer]), reliable: true);

		return Result.Ok<Peer>(peer).WithSuccesses(uplinkResult.Successes);
	}


	public async Task<Result<Peer>> TryJoin(Action<Task<IPEndPoint>> local, Func<Task<IPEndPoint>> remote, float punchTimeout = 30f)
	{
		Guard.Against.NotClient(this);

		Result<P2PSocket> uplinkResult = await TryUplink(local, remote, punchTimeout);

		if (uplinkResult.IsFailed) return Result.Fail(uplinkResult.Errors);

		P2PSocket socket = uplinkResult.Value;
		DirectPeer peer = new(0, socket, socket.RemoteEndPoint);
		Peers.Add(peer);
		c_host = peer;
		MessageQueue.Subscribe(c_host);

		Send<Ping>(new());

		return Result.Ok<Peer>(peer).WithSuccesses(uplinkResult.Successes);
	}

	public void Update()
	{
		MessageQueue.ProcessFrame();
		MessageQueue.SendFrame();
	}

	public void Disconnect(Peer peer)
	{
		Guard.Against.NotHost(this);
		Peers.Remove(peer);
		MessageQueue.Unsubscribe(peer);
		((DirectPeer)peer).Dispose();
	}

	public void Close()
	{
		if (IsHost)
		{
			foreach (Peer peer in Peers.ToList())
			{
				Disconnect(peer);
			}
		} else
		{
			if (c_host is null) return;
			MessageQueue.Unsubscribe(c_host);
			c_host.Dispose();
			Peers.Clear();
		}
	}

	public void Register<T>(NetKey key, Rpc<T> rpc) where T : class, IMessage
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
				Guard.Against.NotClient(this);
				MessageQueue.InvokeRemote(c_host!, message, reliable);
				break;
			case TargetKind.Client:
				Guard.Against.NotHost(this);
				MessageQueue.InvokeRemote(target.Peer!, message, reliable);
				break;
			case TargetKind.AllClients:
				Guard.Against.NotHost(this);
				foreach (Peer peer in Peers)
				{
					MessageQueue.InvokeRemote(peer, message, reliable);
				}
				break;
			case TargetKind.AllClientsExcept:
				Guard.Against.NotHost(this);
				foreach (Peer peer in Peers)
				{
					if (peer == target.Peer) continue;
					MessageQueue.InvokeRemote(peer, message, reliable);
				}
				break;
		}
	}
}
