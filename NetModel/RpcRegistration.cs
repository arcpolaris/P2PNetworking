using System;
using Ardalis.GuardClauses;

namespace NetModel;
internal class RpcRegistration<T>(Rpc<T> rpc) : IRpcRegistration where T : class, IMessage
{
	public Rpc<T> Rpc { get; } = rpc;

	public Type Type => typeof(T);

	public void Invoke(Peer sender, object data)
	{
		var cast = Guard.Against.WrongType<T>(data);
		Rpc.Invoke(sender, cast);
	}
}
