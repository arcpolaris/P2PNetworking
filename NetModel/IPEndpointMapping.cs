using System.Net;

namespace NetModel;

/// <summary>
/// Represents a mapping between a LAN endpoint and its corresponding WAN endpoint
/// </summary>
public record class IPEndpointMapping(IPEndPoint Lan, IPEndPoint Wan)
{
	/// <inheritdoc/>
	public override string ToString() => $"{Lan}/{Wan}";
}
