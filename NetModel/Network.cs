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
	private MessageBus MessageBus { get; init; }
	private MessageQueue MessageQueue { get; init; }

	private DirectPeer? _host = null;
	public Peer? Host { get; private set; }

	public bool IsHost { get; private init; }

	private Network()
	{
		Peers = new List<Peer>();
		MessageBus = new();
		MessageQueue = new(MessageBus);
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

	}

	public void Update()
	{

	}

	public void Send<T>(in SendTarget target, NetKey method, T message)
	{

	}
}
