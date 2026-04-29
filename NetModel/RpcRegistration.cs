using System;

namespace NetModel;

/// <summary>
/// Binds an <see cref="Rpc{T}"/> and provides dynamic typing
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="rpc"></param>
internal class RpcRegistration<T>(Rpc<T> rpc) : IRpcRegistration where T : class, IMessage
{
	public Rpc<T> Rpc { get; } = rpc;

	public Type Type => typeof(T);

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <param name="sender"></param>
	/// <param name="message"></param>
	/// <exception cref="ArgumentException"></exception>
	public void Invoke(Peer sender, object message)
	{
		if (message is not T cast) throw new ArgumentException($"Message was not of type {typeof(T)}", nameof(message));
		Rpc.Invoke(sender, cast);
	}
}
