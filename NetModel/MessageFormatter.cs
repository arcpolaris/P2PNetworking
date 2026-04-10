using System;
using MessagePack;
using MessagePack.Formatters;

namespace NetModel;
internal class MessageFormatter : IMessagePackFormatter<IMessage?>
{
	IMessageLookup Lookup { get; init; }

	public MessageFormatter(IMessageLookup lookup)
	{
		Lookup = lookup;
	}

	public void Serialize(ref MessagePackWriter writer, IMessage? value, MessagePackSerializerOptions options)
	{
		if (value is null)
		{
			writer.WriteNil();
			return;
		}

		Type type = value.GetType();
		NetKey key = Lookup.Lookup(type);

		writer.Write(key);
		MessagePackSerializer.Serialize(type, ref writer, value, options);
	}

	public IMessage Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
	{
		if (reader.TryReadNil()) return null!;
		NetKey key = reader.ReadUInt16();
		Type type = Lookup.Lookup(key);

		return (IMessage)MessagePackSerializer.Deserialize(type, ref reader, options)!;
	}
}
