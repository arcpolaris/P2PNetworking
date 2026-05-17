using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;

namespace NetModel;

public sealed partial class Network
{
	/// <summary>
	/// Creates a new <see cref="Builder"/> for constructing a <see cref="Network"/> instance
	/// </summary>
	public static Builder CreateBuilder() => new();
	
	public static Builder CreateBuilder(params IFormatterResolver[] extraResolvers) => new(extraResolvers);

	/// <summary>
	/// Configures and constructs a <see cref="Network"/> instance.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Register message handlers via <see cref="Register{T}(NetKey, MessageHandler{T})"/> before
	/// calling <see cref="Build"/>. Internal protocol handlers are reserved on keys under 100.
	/// </para>
	/// <para>
	/// Each builder instance may only be built once.
	/// </para>
	/// </remarks>
	public sealed class Builder
	{
		private bool _built;
		private MessageRegistry hostRegistry;
		private MessageRegistry clientRegistry;

		private static void ThrowForHost<T>(Network _, Peer __, T ___) where T : class, IMessage
		{
			throw new InvalidOperationException("This method is only valid for a client");
		}

		/// <summary>
		/// Registers the internal protocol message handlers.
		/// </summary>
		public Builder(IFormatterResolver[]? extraResolvers = null)
		{
			extraResolvers ??= [];
			hostRegistry = new(extraResolvers);
			clientRegistry = new(extraResolvers);
			Register<Ping>(0,
				static (net, sender, ping) => net.SendTo<Pong>(sender, new(ping)),
				static (net, _, ping) => net.Send<Pong>(new(ping)));
			Register<Pong>(1,
				static (net, sender, pong) => net.pingLookup[sender.Id] = (int)pong.Delta.TotalMilliseconds);
			Register<AddPeers>(2,
				ThrowForHost,
				static (net, _, addPeers) => net.peers.AddRange(addPeers.Peers));
			Register<RemovePeers>(3,
				ThrowForHost,
				static (net, _, removePeers) =>
				{
					if (removePeers.Peers.Any(p => p.Id == net.MyId || p.Id == 0))
						net.CloseSocket(net.c_host!);
					else
						foreach (Peer peer in removePeers.Peers)
							net.peers.Remove(peer);
				});
			Register<SetId>(4,
				ThrowForHost,
				static (net, _, setId) => net.MyId = setId.Id);
			Register<Acknowledgement>(5,
				static (net, sender, ack) => net.MessageQueue.ConsumeAck(sender, ack));
			Register<Ring>(6, (_, _, _) => { });
		}

		/// <summary>
		/// Associates a <see cref="Type"/>, <see cref="MessageHandler{T}"/> and <see cref="NetKey"/> key
		/// </summary>
		public Builder Register<T>(NetKey key, MessageHandler<T> rpc) where T : class, IMessage
		{
			ThrowIfPreviouslyBuilt();
			hostRegistry.Register(key, rpc);
			clientRegistry.Register(key, rpc);
			return this;
		}

		/// <inheritdoc cref="Register{T}(ushort, MessageHandler{T})"/>
		/// <param name="forHost">The handler to use if initalized as a host</param>
		/// <param name="forClient">The handler to use if initalized as a client</param>
		/// <param name="key"><inheritdoc cref="Register{T}(ushort, MessageHandler{T})"/></param>
		public Builder Register<T>(NetKey key, MessageHandler<T> forHost, MessageHandler<T> forClient) where T : class, IMessage
		{
			ThrowIfPreviouslyBuilt();
			hostRegistry.Register(key, forHost);
			clientRegistry.Register(key, forClient);
			return this;
		}

		/// <inheritdoc cref="Register{T}(NetKey, MessageHandler{T}, MessageHandler{T})"/>
		/// <remarks>
		/// Equivalent to:
		/// <code>
		/// builder.Register(key,
		///     forHost: (sender, message) =>
		///     {
		///         net.SendToAllExcept(sender, message, reliable);
		///         handler(sender, message);
		///     },
		///     forClient: handler);
		/// </code>
		/// </remarks>
		public Builder RegisterWithForward<T>(NetKey key, MessageHandler<T> handler, bool reliable = false) where T : class, IMessage
		{
			clientRegistry.Register(key, handler);
			hostRegistry.Register<T>(key, (net, sender, message) =>
			{
				net.SendToAllExcept(sender, message, reliable);
				handler(net, sender, message);
			});
			return this;
		}

		/// <summary>
		/// Constructs the <see cref="Network"/> and freezes the message registry.
		/// </summary>
		/// <param name="asHost">
		/// If <see langword="true"/>, the network is initialized as a host; otherwise as a client.
		/// </param>
		public Network Build(bool asHost)
		{
			ThrowIfPreviouslyBuilt();

			MessageRegistry registry = asHost ? hostRegistry : clientRegistry;
			registry.Freeze();
			Network res = new(asHost,registry);

			hostRegistry = null!;
			clientRegistry = null!;
			_built = true;
			return res;
		}

		private void ThrowIfPreviouslyBuilt()
		{
			if (_built)
				throw new InvalidOperationException("Builder is exausted");
		}
	}
}
