using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using NetModel;

namespace TestEngine;

internal sealed class LoopbackHarness : IAsyncDisposable
{
	private readonly List<P2PSocket> sockets = [];
	private readonly List<Task> pollingTasks = [];
	private readonly CancellationTokenSource cts = new();

	public static async Task EventuallyAsync(
		Func<bool> condition,
		Action pump,
		int timeoutMs = 2000,
		int stepMs = 10)
	{
		var started = Environment.TickCount64;

		while (Environment.TickCount64 - started < timeoutMs)
		{
			pump();

			if (condition())
				return;

			await Task.Delay(stepMs);
		}

		pump();
		Assert.IsTrue(condition(), "Timed out waiting for condition.");
	}

	public SocketPair CreateSocketPair()
	{
		P2PSocket left = new();
		P2PSocket right = new();

		left.BindAny();
		right.BindAny();

		left.SetRemote(new IPEndPoint(IPAddress.Loopback, right.LocalEndPoint.Port));
		right.SetRemote(new IPEndPoint(IPAddress.Loopback, left.LocalEndPoint.Port));

		sockets.Add(left);
		sockets.Add(right);

		pollingTasks.Add(left.StartPolling(cts.Token));
		pollingTasks.Add(right.StartPolling(cts.Token));

		return new SocketPair(left, right);
	}

	public ConnectedQueues CreateConnectedQueues<T>(
		ushort messageKey,
		Rpc<T> onLeft,
		Rpc<T> onRight)
		where T : class, IMessage
	{
		var pair = CreateSocketPair();

		MessageRegistry leftRegistry = new();
		MessageRegistry rightRegistry = new();

		leftRegistry.Register(messageKey, onLeft);
		rightRegistry.Register(messageKey, onRight);

		leftRegistry.Freeze();
		rightRegistry.Freeze();

		MessageQueue leftQueue = new(leftRegistry);
		MessageQueue rightQueue = new(rightRegistry);

		DirectPeer leftRemote = new(2, pair.Left, pair.Left.RemoteEndPoint);
		DirectPeer rightRemote = new(1, pair.Right, pair.Right.RemoteEndPoint);

		leftQueue.Subscribe(leftRemote);
		rightQueue.Subscribe(rightRemote);

		return new ConnectedQueues(leftQueue, rightQueue, leftRemote, rightRemote);
	}

	public ConnectedNetworkPair CreateNetworkPair(
		ushort clientId,
		Action<Network>? configureHost = null,
		Action<Network>? configureClient = null)
	{
		SocketPair pair = CreateSocketPair();

		Network host = Network.ConstructHost();
		Network client = Network.ConstructClient();

		configureHost?.Invoke(host);
		configureClient?.Invoke(client);

		SetPrivateProperty(client, "MyId", clientId);

		DirectPeer hostSidePeer = new(clientId, pair.Left, pair.Left.RemoteEndPoint);
		DirectPeer clientHostPeer = new(0, pair.Right, pair.Right.RemoteEndPoint);

		AttachPeer(host, hostSidePeer);
		AttachPeer(client, clientHostPeer);
		SetPrivateField(client, "c_host", clientHostPeer);

		host.FinishSetup();
		client.FinishSetup();

		return new ConnectedNetworkPair(host, client, hostSidePeer, clientHostPeer);
	}

	public MultiClientNetwork CreateMultiClientNetwork(
		params ushort[] clientIds)
	{
		Network host = Network.ConstructHost();
		var clients = new List<ConnectedNetworkPair>();

		foreach (ushort clientId in clientIds)
		{
			SocketPair pair = CreateSocketPair();

			Network client = Network.ConstructClient();
			SetPrivateProperty(client, "MyId", clientId);

			DirectPeer hostSidePeer = new(clientId, pair.Left, pair.Left.RemoteEndPoint);
			DirectPeer clientHostPeer = new(0, pair.Right, pair.Right.RemoteEndPoint);

			AttachPeer(host, hostSidePeer);
			AttachPeer(client, clientHostPeer);
			SetPrivateField(client, "c_host", clientHostPeer);

			clients.Add(new ConnectedNetworkPair(host, client, hostSidePeer, clientHostPeer));
		}

		host.FinishSetup();
		foreach (var client in clients)
			client.Client.FinishSetup();

		return new MultiClientNetwork(host, clients);
	}

	public void Pump(params Network[] networks)
	{
		foreach (Network network in networks)
			network.Update();
	}

	private static void AttachPeer(Network network, DirectPeer peer)
	{
		object peers = GetPrivateField(network, "Peers")!;
		object queue = GetPrivateField(network, "MessageQueue")!;

		((IList)peers).Add(peer);

		MethodInfo subscribe = queue.GetType().GetMethod("Subscribe", BindingFlags.Instance | BindingFlags.Public)!;
		subscribe.Invoke(queue, [peer]);
	}

	private static object? GetPrivateField(object instance, string name)
	{
		return instance.GetType()
			.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(instance);
	}

	private static void SetPrivateField(object instance, string name, object? value)
	{
		instance.GetType()
			.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
			.SetValue(instance, value);
	}

	private static void SetPrivateProperty(object instance, string name, object? value)
	{
		instance.GetType()
			.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
			.SetValue(instance, value);
	}

	public async ValueTask DisposeAsync()
	{
		cts.Cancel();

		try
		{
			await Task.WhenAll(pollingTasks);
		}
		catch
		{
			// ignored for teardown
		}

		foreach (var socket in sockets)
			socket.Dispose();

		cts.Dispose();
	}

	internal readonly record struct SocketPair(P2PSocket Left, P2PSocket Right);

	internal readonly record struct ConnectedQueues(
		MessageQueue LeftQueue,
		MessageQueue RightQueue,
		DirectPeer LeftRemote,
		DirectPeer RightRemote);

	internal readonly record struct ConnectedNetworkPair(
		Network Host,
		Network Client,
		DirectPeer HostSidePeer,
		DirectPeer ClientHostPeer);

	internal sealed record MultiClientNetwork(
		Network Host,
		IReadOnlyList<ConnectedNetworkPair> Clients);
}

[MessagePackObject(AllowPrivate = true)]
internal sealed class TestMessage : IMessage
{
	[Key(0)]
	public string Text { get; set; } = string.Empty;

	[Key(1)]
	public int Number { get; init; }

	public override string ToString() => $"{Text}:{Number}";
}

[MessagePackObject(AllowPrivate = true)]
internal sealed class OrderedMessage : IMessage
{
	[Key(0)]
	public string Label { get; set; } = string.Empty;
}