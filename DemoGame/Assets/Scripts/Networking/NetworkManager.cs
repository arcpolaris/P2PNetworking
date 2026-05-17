using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using DemoGame.Networking;
using MessagePack.Unity;
using NetModel;
using ObservableCollections;
using ParrelSync;
using R3;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Network = NetModel.Network;

public class NetworkManager : MonoBehaviour
{
	public Network.Builder NetworkBuilder { get; private set; } = Network.CreateBuilder(UnityResolver.Instance);
	public Network Network { get; private set; }

	[SerializeField] bool startAsHost;

	[ContextMenuItem("Start Uplinking", nameof(BeginUplink))]
	[ContextMenuItem("Set Remote", nameof(SetRemote))]
	[SerializeField] string remoteEP;

	TaskCompletionSource<IPEndPoint> remoteEPSource = new();

	public static NetworkManager Instance { get; private set; }

	public Dictionary<Guid, NetworkObject> Objects { get; } = new();
	public Dictionary<Guid, NetworkObject> Prefabs { get; } = new();

	private void Awake()
	{
		if (Instance)
		{
			Destroy(this);
			Debug.LogErrorFormat("Only one {0} is allowed per scene", nameof(NetworkManager));
			return;
		}
		Instance = this;
		Trace.Listeners.Add(new UnityTraceListener());

		if (ClonesManager.IsClone())
		{
			string remoteEP = ClonesManager.GetArgument();
			startAsHost = false;
		}
	}

	void Start()
	{
		Network = NetworkBuilder.Build(startAsHost);
		NetworkBuilder = null;
		Network.Peers.ObserveAdd(destroyCancellationToken).Subscribe(addition =>
		{
			Peer peer = addition.Value;

			print("new peer");

			foreach(NetworkObject obj in Objects.Values)
			{
				Network.SendTo<MakePawn>(peer, new(obj), reliable: true);
			}
		});
	}

	void Update()
	{
		Network.Update();
	}

	async void BeginUplink()
	{
		if (startAsHost)
		{
			await Network.Admit(
				local: ep => Debug.Log(ep),
				remote: remoteEPSource.Task,
				30f
			).ConfigureAwait(false);
		} else {
			await Network.Join(
				local: ep => Debug.Log(ep),
				remote: remoteEPSource.Task,
				30f
			).ConfigureAwait(false);
		}
		remoteEPSource = new();
		print(Network.Peers);
	}

	void SetRemote()
	{
		string[] split = remoteEP.Split(':');
		IPAddress address = IPAddress.Parse(split[0]);
		int port = int.Parse(split[1]);
		IPEndPoint endpoint = new(address, port);
		remoteEPSource.SetResult(endpoint);
		remoteEP = "";
	}
}


class UnityTraceListener : TraceListener
{
	public override void Write(string message)
	{
		Debug.Log(message);
	}

	public override void WriteLine(string message)
		=> Write(message);

	public override void Write(object o, string category)
	{
		Debug.LogFormat("{0}: {1}", category, o);
	}

	public override void WriteLine(object o, string category)
		=> Write(o, category);
}