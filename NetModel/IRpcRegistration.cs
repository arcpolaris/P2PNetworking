using System;

namespace NetModel;

internal interface IRpcRegistration
{
	Type Type { get; }
	void Invoke(Peer sender, object data);
}