using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using DemoGame.Networking;
using MessagePack;
using MessagePack.Unity;
using NetModel;
using ObservableCollections;
using ParrelSync;
using R3;
using UnityEngine;
using AYellowpaper.SerializedCollections;
using Debug = UnityEngine.Debug;
using Network = NetModel.Network;

namespace DemoGame
{
	public class NetworkManager : MonoBehaviour
	{
		public Network.Builder NetworkBuilder { get; private set; } = Network.CreateBuilder(
			GeneratedMessagePackResolver.Instance,
			UnityResolver.Instance
			);
		public Network Network { get; private set; }

		[SerializeField] bool uplinkOnStart;
		[SerializeField] bool startAsHost;

		[ContextMenuItem("Start Uplinking", nameof(BeginUplink))]
		[ContextMenuItem("Set Remote", nameof(SetRemote))]
		[SerializeField] string remoteEP;

		TaskCompletionSource<IPEndPoint> remoteEPSource = new();

		public static NetworkManager Instance { get; private set; }


		public SerializedDictionary<string, NetworkObject> Objects = new();
		public SerializedDictionary<string, NetworkObject> Prefabs = new();

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
		}

		private void OnValidate()
		{
			if (ClonesManager.IsClone())
			{
				remoteEP = ClonesManager.GetArgument();
				startAsHost = false;
			}

		}

		void Start()
		{
			Network = NetworkBuilder.Build(startAsHost);
			NetworkBuilder = null;
			Network.Peers.ObserveAdd(destroyCancellationToken).ObserveOnCurrentSynchronizationContext().Subscribe(addition =>
			{
				Peer peer = addition.Value;

				print("new peer: " + peer.Id);
				print("total objects: " + Objects.Values.Count);
				foreach (NetworkObject obj in Objects.Values)
				{
					Network.SendTo<MakePawn>(peer, new(obj), reliable: true);
				}
			});

			StartCoroutine(NetworkRoutine());

			if (uplinkOnStart)
			{
				BeginUplink();
			}
		}

		private void OnDestroy()
		{
			StopAllCoroutines();
		}

		private void OnApplicationQuit()
		{
			StopAllCoroutines();
		}

		IEnumerator NetworkRoutine()
		{
			while (true)
			{
				Network.Update();
				yield return null;
			}
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
			}
			else
			{
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

		public override void Write(string message, string category)
			=> Write((object)message, category);

		public override void WriteLine(string message, string category)
			=> Write((object)message, category);

		public override void Fail(string message)
		{
			Debug.LogError(message);
		}
	}
}