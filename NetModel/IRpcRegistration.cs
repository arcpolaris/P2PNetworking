using System;

namespace NetModel;

/// <summary>
/// Holds an <see cref="Rpc{T}"/> without compile-time typing
/// </summary>
internal interface IRpcRegistration
{
	/// <summary>
	/// Type of the bound <see cref="Rpc{T}"/>
	/// </summary>
	Type Type { get; }
	/// <summary>
	/// Invokes the bound <see cref="Rpc{T}"/>
	/// </summary>
	/// <param name="sender">Remote sender of the message</param>
	/// <param name="message">Message should be of Type <see cref="Type"/></param>
	void Invoke(Peer sender, object message);
}