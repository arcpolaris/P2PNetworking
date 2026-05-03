using NetModel;
using MessagePack;
using UnityEngine;

using Network = NetModel.Network;
using System.Threading.Tasks;
using System.Net;
using System.Diagnostics;

using Debug = UnityEngine.Debug;

public sealed class ChatConsole : MonoBehaviour
{
	[SerializeField]
	[ContextMenuItem("SetRemote", nameof(SetRemote))]
	[ContextMenuItem("Send", nameof(Send))]
	private string prompt = "";

	TaskCompletionSource<IPEndPoint> tcs;

	private void Awake()
	{
		Trace.Listeners.Add(new UnityTraceListener());
		Network.InitializeClient();
		Network.Instance!.Register<TextMessage>(100, (sender, text) =>
		{
			Debug.Log($"[{sender.Id}]: {text.Text}");

			if (Network.Instance.IsHost)
				Network.Instance.SendToAllExcept<IndirectTextMessage>(sender, new(sender, text));
		});

		Network.Instance.Register<IndirectTextMessage>(101, (sender, indirect) =>
		{
			Debug.Log($"[{indirect.From}]: {indirect.Text}");
		});

		Network.Instance.FinishSetup();
	}

	async void Start()
	{
		Debug.Log(await Network.GetPublicIP().ConfigureAwait(false));
		tcs = new();
		await Network.Instance.Join(
			local: ep => Debug.Log(ep),
			remote: tcs.Task,
			30f
		).ConfigureAwait(false);
	}

	void SetRemote()
	{
		string[] split = prompt.Split(':');
		IPAddress address = IPAddress.Parse(split[0]);
		int port = int.Parse(split[1]);
		IPEndPoint endpoint = new IPEndPoint(address, port);
		tcs.SetResult(endpoint);
		prompt = "";
	}

	void Send()
	{
		Network.Instance!.Send(new TextMessage(prompt));
	}

	public void Update()
	{
		Network.Instance?.Update();
	}
}

class UnityTraceListener : TraceListener
{
	public override void Write(string message)
	{
		Debug.Log(message);
	}

	public override void WriteLine(string message)
	{
		Debug.Log(message);
	}
}

[MessagePackObject(AllowPrivate = true)]
public class TextMessage : IMessage
{
	[Key(0)]
	public string Text { get; set; }

	public TextMessage(string text) => Text = text;
}

[MessagePackObject(AllowPrivate = true)]
public class IndirectTextMessage : IMessage
{
	[Key(0)]
	public uint From { get; set; }

	[Key(1)]
	public string Text { get; set; }

	[SerializationConstructor]
	public IndirectTextMessage(uint from, string text)
	{
		From = from;
		Text = text;
	}

	public IndirectTextMessage(Peer from, TextMessage text)
	{
		From = from.Id;
		Text = text.Text;
	}
}