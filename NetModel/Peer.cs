using MessagePack;

namespace NetModel;

[MessagePackObject(AllowPrivate = true)]
public class Peer
{
	[Key(0)]
	public ushort Id { get; private init; }

	[SerializationConstructor]
	internal Peer(ushort id)
	{
		Id = id;
	}

	public override bool Equals(object obj) => obj is Peer other && Id == other.Id;

	public override int GetHashCode() => Id.GetHashCode();
}