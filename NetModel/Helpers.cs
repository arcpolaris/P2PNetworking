global using NetKey = ushort;

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace NetModel;

public static class Helpers
{
	public static async Task<IPAddress> GetPublicIP()
	{
		using HttpClient client = new();
		string ip = await client.GetStringAsync("https://api.ipify.org");
		
		return IPAddress.Parse(ip);
	}

	public static void Deconstruct(this IPEndPoint ep, out IPAddress address, out int port)
	{
		address = ep.Address;
		port = ep.Port;
	}
}
