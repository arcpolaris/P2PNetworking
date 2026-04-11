using System;
using Ardalis.GuardClauses;

namespace NetModel;
internal class RpcRegistration<T>(Rpc<T> rpc) : IRpcRegistration where T : class, IMessage
{
	public Rpc<T> Rpc { get; } = rpc;

	public Type Type => typeof(T);

	public void Invoke(Peer sender, object message)
	{
		var cast = Guard.Against.WrongType<T>(message);
		Rpc.Invoke(sender, cast);
	}
}
