using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using MessagePack;
using MessagePack.Resolvers;

namespace NetModel;

internal class MessageRegistry : IMessageLookup
{
	private IDictionary<NetKey, IRpcRegistration> rpcLookup = new Dictionary<NetKey, IRpcRegistration>();
	private IDictionary<Type, NetKey> typeLookup = new Dictionary<Type, NetKey>();

	private MessagePackSerializerOptions serializerOptions;

	public MessageRegistry()
	{
		var formatter = new MessageFormatter(this);
		var resolver = CompositeResolver.Create(
			[
				formatter
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

	public MessageRegistry Register<T>(NetKey key, Rpc<T> procedure) where T : class, IMessage
	{
		if (rpcLookup.IsReadOnly)
			throw new InvalidOperationException("The registry has been frozen");

		RpcRegistration<T> registration = new(procedure);

		rpcLookup.Add(key, registration);
		typeLookup.Add(typeof(T), key);

		return this;
	}

	internal IRpcRegistration GetRpc(NetKey key) => rpcLookup[key];

	internal void Freeze()
	{
		if (rpcLookup is FrozenDictionary<NetKey, IRpcRegistration>)
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