using System;

namespace NetModel;
internal interface IMessageLookup
{
	Type Lookup(NetKey key);
	NetKey Lookup<T>() where T : class, IMessage;
	NetKey Lookup(Type type);
}