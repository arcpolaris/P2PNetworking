using System.Collections.Generic;
using MessagePack;

namespace NetModel;

[MessagePackObject(AllowPrivate = true)]
internal sealed class Packet
{
	[Key(0)]
	public int Timestamp { get; set; }

	[Key(1)]
	public bool IsReliable { get; set; }

	[Key(2)]
	public List<IMessage> Messages { get; set; } = [];
}
