using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MessagePack;
using MessagePack.Formatters;

namespace NetModel;

[SuppressMessage("Usage", "MsgPack013:Inaccessible formatter", Justification = "Formatter will always be used as an instance")]
[SuppressMessage("CodeQuality", "IDE0079:Remove unnecessary suppression")]
internal sealed class PacketFormatter(MessageRegistry lookup) : IMessagePackFormatter<Packet?>
{
	private MessageRegistry Lookup { get; } = lookup;

	public void Serialize(ref MessagePackWriter writer, Packet? value, MessagePackSerializerOptions options)
	{
		if (value is null)
		{
			writer.WriteNil();
			return;
		}

		writer.WriteArrayHeader(3);

		writer.WriteInt32(value.Sequence);
		writer.Write(value.IsReliable);

		writer.WriteArrayHeader(value.Messages.Count);
		foreach (IMessage message in value.Messages)
		{
			Type type = message.GetType();
			NetKey key = Lookup.Lookup(type);

			writer.Write(key);
			MessagePackSerializer.Serialize(type, ref writer, message, options);
		}
	}

	public Packet? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
	{
		if (reader.TryReadNil()) return null;

		int count = reader.ReadArrayHeader();
		if (count != 3)
			throw new MessagePackSerializationException($"Invalid Packet field count: {count}");

		Packet packet = new()
		{
			Sequence = reader.ReadInt32(),
			IsReliable = reader.ReadBoolean()
		};

		int messageCount = reader.ReadArrayHeader();
		packet.Messages = new List<IMessage>(messageCount);

		for (int i = 0; i < messageCount; i++)
		{
			NetKey key = reader.ReadUInt16();
			Type type = Lookup.Lookup(key);
			IMessage message = (IMessage)MessagePackSerializer.Deserialize(type, ref reader, options)!;
			if (message is Acknowledgement ack) ack.Sequence = packet.Sequence;
			packet.Messages.Add(message);
		}

		return packet;
	}
}