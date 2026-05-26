using NetModel;
using MessagePack;
using IngameDebugConsole;
using DemoGame.Util;

namespace DemoGame.Networking
{
	public sealed class ChatConsole : Singleton<ChatConsole>
	{
		protected override void OnInitialize()
		{
			NetworkManager.Instance.NetworkBuilder.Register<TextMessage>(100,
				static (net, sender, text) => {
				print($"[{sender.Id}]: {text.Text}");
				net.SendToAllExcept<IndirectTextMessage>(sender, new(sender, text));
			}, static (_, sender, text) =>
				print($"[{sender.Id}]: {text.Text}")
			).Register<IndirectTextMessage>(101, static (_, sender, indirect) =>
				print($"[{indirect.From}]: {indirect.Text}")
			);
		}

		[ConsoleMethod("say", "Sends a text message to the room", "message")]
		public static void Send(string prompt)
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