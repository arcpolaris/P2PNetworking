namespace NetModel
{
	/// <summary>
	/// Represents a target for message delivery
	/// </summary>
	public readonly record struct SendTarget(TargetKind Kind, Peer? Peer = null);

/// <summary>
/// Represents a target for message delivery
/// </summary>
public readonly record struct SendTarget(TargetKind Kind, Peer? Peer = null);

/// <summary>
/// Specifies how a <see cref="SendTarget"/> selects message recipients
/// </summary>
public enum TargetKind
{
	/// <summary>
	/// Send only to the remote host
	/// </summary>
	Host,
	/// <summary>
	/// Send only to a specific client
	/// </summary>
	Client,
	/// <summary>
	/// Send to all clients
	/// </summary>
	AllClients,
	/// <summary>
	/// Send to all clients except one
	/// </summary>
	AllClientsExcept
}