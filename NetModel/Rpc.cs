namespace NetModel;

public delegate void Rpc<T>(Peer sender, T message) where T : class, IMessage;