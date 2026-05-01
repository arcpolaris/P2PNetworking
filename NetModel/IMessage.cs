using MessagePack;

namespace NetModel;

/// <summary>
/// Represents a network operation that can be serialized along with its parameters
/// </summary>
/// <remarks>
/// Always mark with a <see cref="MessagePackObjectAttribute"/>
/// </remarks>
public interface IMessage;