using System;

namespace NetModel;

/// <summary>
/// Holds a <see cref="MessageHandler{T}"/> without compile-time typing
/// </summary>
internal interface IMessageHandler
{
	/// <summary>
	/// Type of the bound <see cref="MessageHandler{T}"/>
	/// </summary>
	Type Type { get; }
	/// <summary>
	/// Invokes the bound <see cref="MessageHandler{T}"/>
	/// </summary>
	/// <param name="sender">Remote sender of the message</param>
	/// <param name="message">Message should be of Type <see cref="Type"/></param>
	void Invoke(Peer sender, object message);
}