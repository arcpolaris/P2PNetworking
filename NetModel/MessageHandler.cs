namespace NetModel;

/// <summary>
/// Handles a recieved <see cref="IMessage"/> of type <typeparamref name="T"/>
/// </summary>
/// <remarks>
///	<para>
///	Message delivery <b>does not guarantee</b> deduplicatation nor ordering
///	</para>
///	<para>
///	As such, handlers should be both idempotent and order-invarient
/// </para>
/// </remarks>
public delegate void MessageHandler<T>(Peer sender, T message) where T : class, IMessage;