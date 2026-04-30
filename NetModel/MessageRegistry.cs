using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using MessagePack;
using MessagePack.Resolvers;

namespace NetModel;

internal class MessageRegistry
{
	private IDictionary<NetKey, IMessageHandler> rpcLookup = new Dictionary<NetKey, IMessageHandler>();
	private IDictionary<Type, NetKey> typeLookup = new Dictionary<Type, NetKey>();

	private MessagePackSerializerOptions serializerOptions;

	public MessageRegistry()
	{
		var messageFormatter = new MessageFormatter(this);
		var packetFormatter = new PacketFormatter(this);

		var resolver = CompositeResolver.Create(
			[
				messageFormatter,
				packetFormatter
			],
			[
				StandardResolver.Instance
			]
		);

		serializerOptions = MessagePackSerializerOptions.Standard.WithResolver(resolver);
	}

	public Type Lookup(NetKey key)
	{
		return rpcLookup[key].Type;
	}

	public NetKey Lookup(Type type)
	{
		return typeLookup[type];
	}

	public MessageRegistry Register<T>(NetKey key, MessageHandler<T> procedure) where T : class, IMessage
	{
		if (rpcLookup.IsReadOnly)
			throw new InvalidOperationException("The registry has been frozen");

		MessageHandlerRegistration<T> registration = new(procedure);

		rpcLookup.Add(key, registration);
		typeLookup.Add(typeof(T), key);

		return this;
	}

	internal IMessageHandler GetRpc(NetKey key) => rpcLookup[key];

	internal void Freeze()
	{
		if (rpcLookup is FrozenDictionary<NetKey, IMessageHandler>)
			return;

		rpcLookup = rpcLookup.ToFrozenDictionary();
		typeLookup = typeLookup.ToFrozenDictionary();
	}

	internal byte[] Marshal(Packet packet)
	{
		return MessagePackSerializer.Serialize<Packet>(packet, serializerOptions);
	}

	internal Packet Digest(ArraySegment<byte> bytes)
	{
		return MessagePackSerializer.Deserialize<Packet>(bytes, serializerOptions);
	}
}