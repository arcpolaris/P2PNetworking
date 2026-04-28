using System;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentResults;
using MessagePack;
using NetModel;

using static NetModel.Helpers;

namespace Chatroom;

internal static class Program
{
	static bool running = false;

	static CancellationTokenSource? pollingCts = null;

	static async Task<int> Main(string[] args)
	{
		return await BuildRoot().Parse(args).InvokeAsync();
	}

	static void Setup()
	{
		Network.Instance!.Register<TextMessage>(100, (sender, text) =>
		{
			Console.WriteLine("[{0}]: {1}", sender.Id, text.Text);

			if (Network.Instance.IsHost) Network.Instance.SendToAllExcept<IndirectTextMessage>(sender, new(sender, text));
		});
		Network.Instance.Register<IndirectTextMessage>(101, (sender, indicrect) =>
		{
			Console.WriteLine("[{0}]: {1}", indicrect.From, indicrect.Text);
		});
		Network.Instance.FinishSetup();

		pollingCts?.Dispose();
		pollingCts = new();
		Task.Run(async () =>
		{
			while (!pollingCts.Token.IsCancellationRequested)
			{
				Network.Instance?.Update();
				await Task.Delay(20);
			}
		});
	}

	static Command BuildRoot()
	{
		Command root_cmd = new("pchat", "P2P Chatroom Service over UDP")
		{
			new HelpOption()
		};
		root_cmd.SetAction(parseResult => { new HelpAction().Invoke(parseResult); return 0; });

		Command getip_cmd = new("myip", "Displays the working IP address (IPv4)")
		{
		};
		getip_cmd.SetAction(async _ => { var ip = await GetPublicIP(); Console.WriteLine(ip); return 0; });
		root_cmd.Add(getip_cmd);

		Option<double> timeout_opt = new("timeout", "-t", "--timeout")
		{
			Description = "Timeout in seconds after which the operation will be cancelled",
			Arity = ArgumentArity.ExactlyOne,
			DefaultValueFactory = _ => 15
		};

		if (running)
		{
			Command exit_cmd = new("exit", "Exit interactive shell");
			exit_cmd.SetAction(_ =>
			{
				running = false;
				Network.Instance?.Dispose();
			});
			root_cmd.Add(exit_cmd);

			Command start_cmd = new("start", "Initalize Network");
			root_cmd.Add(start_cmd);

			Command start_host_cmd = new("host", "Start Network as host");
			start_host_cmd.SetAction(_ => { Network.InitializeHost(); Setup(); });
			start_cmd.Add(start_host_cmd);

			Command start_cmdlient_cmd = new("client", "Start Network as client");
			start_cmdlient_cmd.SetAction(_ => { Network.InitalizeClient(); Setup();});
			start_cmd.Add(start_cmdlient_cmd);

			Command admit_cmd = new("admit", "Admit a client to your chatroom")
			{
				timeout_opt
			};
			admit_cmd.SetAction(async parsed =>
			{
				float timeout = (float)parsed.GetValue(timeout_opt);
				if (timeout < 0) timeout = 30;

				Result<Peer> result = await Network.Instance!.TryAdmit(local: ep =>
				{
					Console.WriteLine("Admitting on {0}\n", ep);
				}, remote: Task.Run(async () =>
				{
					string? raw = null;
					Console.WriteLine("Enter Remote Endpoint: ");
					do raw = await Console.In.ReadLineAsync();
					while (string.IsNullOrWhiteSpace(raw));

					return IPEndPoint.Parse(raw);
				}), timeout);

				if (result.IsSuccess)
				{
					Console.WriteLine("Connection esablished with Peer [{0}]", result.Value.Id);
				} else
				{
					Console.Error.WriteLine(string.Join("\n", result.Errors.Select(error => error.Message)));
				}
			});
			root_cmd.Add(admit_cmd);

			Command join_cmd = new("join", "Join a remote chatroom")
			{
				timeout_opt
			};
			join_cmd.SetAction(async parsed =>
			{
				float timeout = (float)parsed.GetValue(timeout_opt);
				if (timeout < 0) timeout = 30;

				Result<Peer> result = await Network.Instance!.TryJoin(local: ep =>
				{
					Console.WriteLine("Waiting on {0}", ep);
				}, remote: Task.Run(async () =>
				{
					string? raw = null;
					Console.WriteLine("Enter Remote Endpoint: ");
					do raw = await Console.In.ReadLineAsync();
					while (string.IsNullOrWhiteSpace(raw));

					return IPEndPoint.Parse(raw);
				}), timeout);

				if (result.IsSuccess)
				{
					Console.WriteLine("Connection esablished with host");
				}
				else
				{
					Console.Error.WriteLine(string.Join("\n", result.Errors.Select(error => error.Message)));
				}
			});
			root_cmd.Add(join_cmd);
			
			Argument<string> message_arg = new("message")
			{
				Description = "Plaintext message to send",
				Arity = ArgumentArity.ExactlyOne
			};

			Command broadcast_cmd = new("say", "Say something to the chatroom")
			{
				message_arg
			};
			broadcast_cmd.SetAction(parsed =>
			{
				string message = parsed.GetRequiredValue(message_arg);
				Network.Instance!.Send<TextMessage>(new(message));
			});
			root_cmd.Add(broadcast_cmd);
		}
		else
		{
			Command shell_cmd = new("repl", "Start interactive shell")
			{
			};
			shell_cmd.SetAction(async _ =>
			{
				running = true;
				while (running)
				{
					Console.Write("> ");
					string? line = Console.ReadLine();
					if (string.IsNullOrWhiteSpace(line))
						continue;

					string[] argv = CommandLineParser.SplitCommandLine(line).ToArray();

					await BuildRoot().Parse(argv).InvokeAsync();
				}
			});
			root_cmd.Add(shell_cmd);
		}

		return root_cmd;
	}
}

[MessagePackObject(AllowPrivate = true)]
internal class TextMessage : IMessage
{
	[Key(0)]
	public string Text { get; set; }

	public TextMessage(string text) => Text = text;
}

[MessagePackObject(AllowPrivate = true)]
internal class IndirectTextMessage : IMessage
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