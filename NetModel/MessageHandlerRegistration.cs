using System;

namespace NetModel;

/// <summary>
/// Binds an <see cref="MessageHandler{T}"/> and provides dynamic typing
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="rpc"></param>
internal class MessageHandlerRegistration<T>(MessageHandler<T> rpc) : IMessageHandler where T : class, IMessage
{
	public MessageHandler<T> Rpc { get; } = rpc;

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
