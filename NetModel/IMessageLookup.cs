using System;

namespace NetModel;

internal interface IMessageLookup
{
	Type Lookup(NetKey key);
	NetKey Lookup(Type type);
}