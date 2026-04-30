namespace NetModel;

public delegate void MessageHandler<T>(Peer sender, T message) where T : class, IMessage;