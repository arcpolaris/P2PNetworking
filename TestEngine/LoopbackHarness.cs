using System.Diagnostics;
using System.Net;
using FluentResults;
using MessagePack;
using NetModel;

namespace TestEngine;

internal sealed class LoopbackHarness : IDisposable
{
	private readonly HashSet<Network> networks = [];

	public static async Task EventuallyAsync(
		Func<bool> condition,
		Action pump,
		int timeoutMs = 4000,
		Action? onTimeout = null,
		int stepMs = 10)
	{
		long started = Environment.TickCount64;

		while (Environment.TickCount64 - started < timeoutMs)
		{
			pump();

			if (condition())
				return;

			await Task.Delay(stepMs);
		}

		pump();

		if (onTimeout is null)
			Assert.IsTrue(condition(), "Timed out waiting for condition.");
		else onTimeout.Invoke();
	}

	public async Task<ConnectedNetworkPair> CreateNetworkPairAsync(
		Action<Network>? configure,
		float punchTimeout = 15f)
	{
		Network host = Network.ConstructHost();
		Network client = Network.ConstructClient();

		configure?.Invoke(host);
		configure?.Invoke(client);

		host.FinishSetup();
		client.FinishSetup();

		networks.Add(host);
		networks.Add(client);

		ConnectedNetworkPair pair = await ConnectAsync(host, client, punchTimeout);

		await EventuallyAsync(
			condition: () =>
			{
				Debug.WriteLine($"{client.MyId} <> {pair.Host.Peers[0].Id}");
				return client.MyId == pair.Host.Peers[0].Id;
			},
			pump: Pump,
			timeoutMs: 4000,
			onTimeout: () => Assert.Inconclusive("Connection was not established"));

		return pair;
	}

	public async Task<MultiClientNetwork> CreateMultiClientNetworkAsync(
		int clientCount,
		Action<Network>? configure = null,
		float punchTimeout = 3f)
	{
		Network host = Network.ConstructHost();
		configure?.Invoke(host);
		host.FinishSetup();

		networks.Add(host);

		List<Network> clients = [];

		for (int i = 0; i < clientCount; i++)
		{
			Network client = Network.ConstructClient();
			configure?.Invoke(client);
			client.FinishSetup();

			networks.Add(client);

			await ConnectAsync(host, client, punchTimeout);
			clients.Add(client);

		}
		await EventuallyAsync(
			condition: () => clients.Select(n => n.MyId)
						   .OfType<ushort>()
						   .OrderBy(x => x)
						   .SequenceEqual(host.Peers.Select(p => p.Id).OrderBy(x => x)),
			pump: Pump,
			timeoutMs: 4000,
			onTimeout: () => Assert.Inconclusive("Connection was not established"));

		return new MultiClientNetwork(host, clients);
	}

	public void Pump()
	{
		foreach (Network network in networks)
			network.Update();
	}

	private static async Task<ConnectedNetworkPair> ConnectAsync(
	Network host,
	Network client,
	float punchTimeout)
	{
		TaskCompletionSource<IPEndPoint> hostEndpoint =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<IPEndPoint> clientEndpoint =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		Task<Result<Peer>> admitTask = host.TryAdmit(
			local: hostEndpoint.SetResult,
			remote: clientEndpoint.Task,
			punchTimeout: punchTimeout);

		Task<Result<Peer>> joinTask = client.TryJoin(
			local: clientEndpoint.SetResult,
			remote: hostEndpoint.Task,
			punchTimeout: punchTimeout);

		await Task.WhenAll(admitTask, joinTask);

		Assert.IsTrue(admitTask.Result.IsSuccess,
			string.Join(Environment.NewLine, admitTask.Result.Errors.Select(e => e.Message)));
		Assert.IsTrue(joinTask.Result.IsSuccess,
			string.Join(Environment.NewLine, joinTask.Result.Errors.Select(e => e.Message)));

		return new ConnectedNetworkPair(
			host,
			client);
	}

	public void Dispose()
	{
		foreach (Network network in networks)
			network.Dispose();
		networks.Clear();
	}

	internal readonly record struct ConnectedNetworkPair(
		Network Host,
		Network Client);

	internal sealed record MultiClientNetwork(
		Network Host,
		IReadOnlyList<Network> Clients);
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