using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using DemoGame.Networking;
using DemoGame.Util;
using IngameDebugConsole;
using MessagePack;
using MessagePack.Unity;
using NetModel;
using ObservableCollections;
using R3;
using Debug = UnityEngine.Debug;
using Network = NetModel.Network;

namespace DemoGame
{
	public class NetworkManager : Singleton<NetworkManager>
	{
		public Network.Builder NetworkBuilder { get; private set; } = Network.CreateBuilder(
			GeneratedMessagePackResolver.Instance,
			UnityResolver.Instance
			);
		public Network Network { get; private set; } = null;

		TaskCompletionSource<IPEndPoint> remoteEPSource = new();
		bool awaitingEP = false;

		public Dictionary<string, NetworkObject> Objects = new();
		public Dictionary<string, NetworkObject> Prefabs = new();

		protected override void OnInitialize()
		{
			Trace.Listeners.Add(new UnityTraceListener("sent", "recv"));
		}

		private void Update()
		{
			if (Network == null) return;
			Network.Update();
		}

		void BuildNetwork(bool asHost)
		{
			Network = NetworkBuilder.Build(asHost);
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
		}

		async Task BeginAdmit()
		{
			if (awaitingEP)
			{
				Debug.LogError("Cannot start uplinking - another is in progress");
				return;
			}
			awaitingEP = true;
			try
			{
				await Network.Admit(
					local: ep => Debug.Log("Admitting on: " + ep),
					remote: remoteEPSource.Task,
					30f);
			} finally
			{
				remoteEPSource = new();
				awaitingEP = false;
			}
		}

		async Task BeginJoin()
		{
			if (awaitingEP)
			{
				Debug.LogError("Cannot start uplinking - another is in progress");
				return;
			}
			awaitingEP = true;
			try
			{
				await Network.Join(
					local: ep => Debug.Log("Joining on:" + ep),
					remote: remoteEPSource.Task,
					30f);
			} finally
			{
				remoteEPSource = new();
				awaitingEP = false;
			}
		}

		public enum NetworkMode
		{
			Host = 0,
			Client = 1
		}

		[ConsoleMethod("start", "Start the network", "mode")]
		public static void BuildNetworkStatic(NetworkMode asHost)
		{
			Instance.BuildNetwork(asHost is NetworkMode.Host);
		}

		[ConsoleMethod("admit", "Begin admission of a new client")]
		public static async void BeginAdmitStatic()
		{
			await Instance.BeginAdmit();
		}

		[ConsoleMethod("join", "Begin joining a host")]
		public static async void BeginJoinStatic()
		{
			await Instance.BeginJoin();
		}

		[ConsoleMethod("resolve", "Resolve remote endpoint for join/uplink", "remote")]
		public static void SetEndpoint(IPEndPoint remoteEP)
		{
			Instance.remoteEPSource.SetResult(remoteEP);
		}

		public static IPEndPoint ParseIPEndPoint(string epString)
		{
			string[] split = epString.Split(':');
			if (split.Length != 2) throw new ArgumentException("Port must be delimited with a single ':'");
			var address = IPAddress.Parse(split[0]);
			int port = int.Parse(split[1]);
			return new(address, port);
		}

		[IngameDebugConsole.ConsoleCustomTypeParser(typeof(IPEndPoint))]
		public static bool ParseIPEndPoint(string input, out object output)
		{
			try
			{
				output = ParseIPEndPoint(input);
				return true;
			} catch
			{
				output = null;
				return false;
			}
		}
	}
}