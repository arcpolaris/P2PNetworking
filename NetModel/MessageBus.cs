using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using Ardalis.GuardClauses;
using FluentResults;
using MessagePack;
using MessagePack.Resolvers;

namespace NetModel;

internal class MessageBus : IMessageLookup
{
	private IDictionary<NetKey, IRpcRegistration> registry = new Dictionary<NetKey, IRpcRegistration>();
	private IDictionary<Type, NetKey> typeLookup = new Dictionary<Type, NetKey>();

	private MessagePackSerializerOptions serializerOptions;

	public MessageBus()
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
		return registry[key].Type;
	}

	public NetKey Lookup<T>() where T : class, IMessage
	{
		return typeLookup[typeof(T)];
	}

	public NetKey Lookup(Type type)
	{
		return typeLookup[type];
	}

	public void Register<T>(NetKey key, Rpc<T> procedure) where T : class, IMessage
	{
		if (registry.IsReadOnly)
			throw new InvalidOperationException("The registry has been frozen");

		RpcRegistration<T> registration = new(procedure);

		registry.Add(key, registration);
		typeLookup.Add(typeof(T), key);
	}

	internal void Freeze()
	{
		if (registry is FrozenDictionary<NetKey, IRpcRegistration>)
			return;

		registry = registry.ToFrozenDictionary();
		typeLookup = typeLookup.ToFrozenDictionary();
	}

	internal Packet Digest(ArraySegment<byte> bytes)
	{
		return MessagePackSerializer.Deserialize<Packet>(bytes, serializerOptions);
	}
}