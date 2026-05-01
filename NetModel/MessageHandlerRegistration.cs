using System;

namespace NetModel;

/// <summary>
/// Binds an <see cref="MessageHandler{T}"/> and provides dynamic typing
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="handler"></param>
internal class MessageHandlerRegistration<T>(MessageHandler<T> handler) : IMessageHandlerRegistration where T : class, IMessage
{
	/// <inheritdoc/>
	public MessageHandler<T> Handler { get; } = handler;

	/// <inheritdoc/>
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
		Handler.Invoke(sender, cast);
	}
}
