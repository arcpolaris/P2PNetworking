using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentResults;
using Ardalis.GuardClauses;
using System.Net;
using System.Threading;
using System.Linq;
using ObservableCollections;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace NetModel;

public sealed class Network : IDisposable
{
	public static Network? Instance { get; private set; }

	private ObservableList<Peer> m_Peers { get; init; }
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

	public ISynchronizedViewList<Peer> Peers { get; private init; }

	private Network()
	{
		m_Peers = new ObservableList<Peer>();
		Peers = m_Peers.CreateView(static p => p).ToViewList();		

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
			m_Peers.AddRange(addPeers.Peers);
		})
		.Register<RemovePeers>(3, (sender, removePeers) =>
		{
			Guard.Against.NotClient(this);

			if (removePeers.Peers.Any(p => p.Id == MyId || p.Id == 0)) CloseSocket(c_host!);
			else
			{
				foreach (Peer peer in removePeers.Peers)
				{
					m_Peers.Remove(peer);
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

	public static void InitalizeClient()
	{
		ThrowIfAlreadyInitialized();
		Instance = ConstructClient();
	}

	public void FinishSetup()
	{
		MessageRegistry.Freeze();
	}

	private static async Task<Result<P2PSocket>> TryUplink(Action<IPEndPoint> local, Task<IPEndPoint> remote, float punchTimeout)
	{
		P2PSocket socket = new();
		socket.BindAny();
		IPEndPoint stun = await socket.STUN();
		local(stun);
		var remoteEP = await remote;
		socket.SetRemote(remoteEP);

		bool got_msg = false; // our hole is punched
		bool got_ack = false;

		//if either peer gets an ack, we're good
		;
		byte[] msg = "MESSAGE_"u8.ToArray();
		byte[] ack = "ACKNKOLG"u8.ToArray();

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

		socket.OnMessageRecieved += TempMessageHandler;
		cts.CancelAfter((int)(1000 * punchTimeout));

		_ = socket.StartPolling();
		await socket.HolePunch(GetProbe, cts.Token);

		Result<P2PSocket> result = new();

		if (!got_msg)
		{
			socket.Dispose();
			return result.WithError("Local socket was not punched into");
		}

		socket.OnMessageRecieved -= TempMessageHandler;
		return socket;
	}

	// VERY NOT THREAD SAFE
	public async Task<Result<Peer>> TryAdmit(Action<IPEndPoint> local, Task<IPEndPoint> remote, float punchTimeout = 30f)
	{
		Guard.Against.NotHost(this);

		Result<P2PSocket> uplinkResult = await TryUplink(local, remote, punchTimeout);

		if (uplinkResult.IsFailed) return Result.Fail(uplinkResult.Errors);

		P2PSocket socket = uplinkResult.Value;
		DirectPeer peer = new(++h_peer_counter, socket, socket.RemoteEndPoint);
		m_Peers.Add(peer);
		MessageQueue.Subscribe(peer);

		SendTo<SetId>(peer, new(peer.Id), reliable: true);
		SendTo<AddPeers>(peer, new(m_Peers.Except([peer])), reliable: true);
		SendToAllExcept<AddPeers>(peer, new([peer]), reliable: true);

		return Result.Ok<Peer>(peer).WithSuccesses(uplinkResult.Successes);
	}


	public async Task<Result<Peer>> TryJoin(Action<IPEndPoint> local, Task<IPEndPoint> remote, float punchTimeout = 30f)
	{
		Guard.Against.NotClient(this);

		Result<P2PSocket> uplinkResult = await TryUplink(local, remote, punchTimeout);

		if (uplinkResult.IsFailed) return Result.Fail(uplinkResult.Errors);

		P2PSocket socket = uplinkResult.Value;
		DirectPeer peer = new(0, socket, socket.RemoteEndPoint);
		m_Peers.Add(peer);
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

	public void Kick(Peer peer)
	{
		Guard.Against.NotHost(this);
		//WARNING: we don't tell someone when they have been kicked
		SendToAllExcept(peer, new RemovePeers(peer), true);
		CloseSocket((DirectPeer)peer);
	}

	private void CloseSocket(DirectPeer peer)
	{
		MessageQueue.Remove(peer);
		m_Peers.Remove(peer);
		peer.Dispose();
	}

	public void Disconnect()
	{
		if (IsHost)
		{
			foreach (Peer peer in m_Peers.ToList())
			{
				CloseSocket((DirectPeer)peer);
			}
			Send<RemovePeers>(new(new Peer(0)), true);
		} else
		{
			if (c_host is null) return;
			Send<RemovePeers>(new(new Peer((ushort)MyId!)), true);
			CloseSocket(c_host);
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
				foreach (Peer peer in m_Peers)
				{
					MessageQueue.InvokeRemote(peer, message, reliable);
				}
				break;
			case TargetKind.AllClientsExcept:
				Guard.Against.NotHost(this);
				foreach (Peer peer in m_Peers)
				{
					if (peer == target.Peer) continue;
					MessageQueue.InvokeRemote(peer, message, reliable);
				}
				break;
		}
	}

	public void Dispose()
	{
		m_Peers.OfType<DirectPeer>().Select(p => p.Socket).ToList().ForEach(p => p.Dispose());
		m_Peers.Clear();
	}
}
