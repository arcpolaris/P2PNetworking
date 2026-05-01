using MessagePack;

namespace NetModel;

/// <summary>
/// Represents a remote participant in the network.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="Peer"/> is an abstract handle used to identify and communicate
/// with another participant. It does not guarantee a stable transport-level
/// connection and may become invalid if the peer disconnects.
/// </para>
/// <para>
/// Instances are typically created and managed by the <c>Network</c> and should
/// not be constructed directly.
/// </para>
/// </remarks>
[MessagePackObject(AllowPrivate = true)]
public class Peer
{
	/// <summary>
	/// Gets the unique identifier for this peer within the current network session.
	/// </summary>
	/// <remarks>
	/// Ids are not reused after a peer disconnects
	/// </remarks>
	[Key(0)]
	public NetKey Id { get; private init; }

	[SerializationConstructor]
	internal Peer(NetKey id)
	{
		Id = id;
	}

	/// <summary>
	/// <inheritdoc/>
	/// </summary>
	/// <returns>
	/// <see langword="true"/> if <paramref name="obj"/> is a <see cref="Peer"/> and shares an Id with the current instance
	/// </returns>
	public override bool Equals(object obj) => obj is Peer other && Id == other.Id;

	/// <inheritdoc/>
	public override int GetHashCode() => Id.GetHashCode();
}