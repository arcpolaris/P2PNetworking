using NetModel;
using MessagePack;
using UnityEngine;

namespace DemoGame.Networking
{
	public sealed class ChatConsole : MonoBehaviour
	{
		[SerializeField]
		[ContextMenuItem("Send", nameof(Send))]
		private string prompt = "";

		private void Awake()
		{
			NetworkManager.Instance.NetworkBuilder.Register<TextMessage>(100, static (net, sender, text) =>
			{
				print($"[{sender.Id}]: {text.Text}");
				net.SendToAllExcept<IndirectTextMessage>(sender, new(sender, text));
			}, static (_, sender, text) =>
				print($"[{sender.Id}]: {text.Text}")
			).Register<IndirectTextMessage>(101, static (_, sender, indirect) =>
				print($"[{indirect.From}]: {indirect.Text}")
			);
		}

		void Send()
		{
			NetworkManager.Instance.Network.Send(new TextMessage(prompt), reliable: true);
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
}