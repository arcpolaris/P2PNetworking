using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
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
}
