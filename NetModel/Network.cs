using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentResults;
using Ardalis.GuardClauses;
using MessagePack.Resolvers;
using MessagePack.Formatters;
using MessagePack;

namespace NetModel;

public sealed class Network
{
	public static Network? Instance { get; private set; }

	private List<Peer> Peers { get; init; }
	private MessageRegistry MessageRegistry { get; init; }
	private MessageQueue MessageQueue { get; init; }

	private DirectPeer? _host = null;
	public Peer? Host
	{
		get
		{
			Guard.Against.NotClient(this);
			return _host;
		}
	}

	public bool IsHost { get; private init; }

	private Network()
	{
		Peers = new List<Peer>();
		MessageRegistry = new();
		MessageQueue = new(MessageRegistry);
	}

	private static void ThrowIfAlreadyInitialized()
	{
		if (Instance is not null)
		{
			throw new InvalidOperationException("Network singleton is already initialized");
		}
	}

	public static void InitializeHost()
	{
		ThrowIfAlreadyInitialized();

		Instance = new Network()
		{
			IsHost = true,
		};
	}

	public static void InitalizeClient()
	{
		ThrowIfAlreadyInitialized();

		Instance = new Network()
		{
			IsHost = false,
		};
	}

	public async Task<Result<Peer>> TryAdmit(float timeout)
	{
		Guard.Against.NotHost(this);

	}

	public async Task<Result<Peer>> TryJoin(float timeout)
	{
		Guard.Against.NotClient(this);

	}

	public void Update()
	{

	}

	public void Register<T>(NetKey key, Rpc<T> rpc) where T : class, IMessage
	{
		MessageRegistry.Register<T>(key, rpc);
	}

	public void Send<T>(in SendTarget target, T message, bool reliable = false) where T : class, IMessage
	{
		switch (target.Kind)
		{
			case TargetKind.Host:
				Guard.Against.NotClient(this);
				MessageQueue.InvokeRemote(_host!, message, reliable);
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
