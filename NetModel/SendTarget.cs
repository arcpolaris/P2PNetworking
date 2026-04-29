namespace NetModel;

public readonly record struct SendTarget(TargetKind Kind, Peer? Peer);

public enum TargetKind
{
	Host,
	Client,
	AllClients,
	AllClientsExcept
}