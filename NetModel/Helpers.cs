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

	public static async Task<IPEndPoint> STUN(this P2PSocket sock)
	{
		Socket _socket = sock._socket;
		IPAddress stunIP = (await Dns.GetHostAddressesAsync("stun.l.google.com")).First(addr => addr.AddressFamily == AddressFamily.InterNetwork);
		IPEndPoint stunEP = new(stunIP, 19302);

		byte[] buffer = new byte[52];
		ArraySegment<byte> sendBuffer = new(buffer, 0, 20);
		ArraySegment<byte> recBuffer = new(buffer, 20, 32);

		const uint MAGIC_COOKIE = 0x2112A442;
		Random rnd = new();

		buffer[1] = 0x01;
		BitConverter.TryWriteBytes(sendBuffer.Slice(4, 4), MAGIC_COOKIE);

		rnd.NextBytes(sendBuffer.Slice(8, 12));

		await _socket.SendToAsync(sendBuffer, SocketFlags.None, stunEP);

		int read = await Task.Run(() =>
		{
			var oldTimeout = _socket.ReceiveTimeout;
			var oldBlocking = _socket.Blocking;
			_socket.Blocking = true;
			_socket.ReceiveTimeout = 5000;
			int read = _socket.Receive(recBuffer);
			_socket.Blocking = oldBlocking;
			_socket.ReceiveTimeout = oldTimeout;
			return read;
		});

		if (read == 0) throw new Exception("No response");

		using MemoryStream stream = new(buffer, 20, 32);
		using BinaryReader reader = new(stream);

		if (reader.ReadUInt16() != 0x0101) throw new Exception("Not binding success");
		if (reader.ReadUInt16() != 0x0C00) throw new Exception("Wrong length");
		if (reader.ReadUInt32() != MAGIC_COOKIE) throw new Exception("Magic cookie missing");
		if (!reader.ReadBytes(12).AsSpan().SequenceEqual(sendBuffer.Slice(8, 12))) throw new Exception("Wrong transaction ID");
		if (reader.ReadUInt16() != 0x0100) throw new Exception("Missing ADDRESS attribute");
		if (reader.ReadUInt16() != 0x0800) throw new Exception("Wrong length");
		if (reader.ReadByte() != 0x00) throw new Exception("Required zero");
		if (reader.ReadByte() != 0x01) throw new Exception("IPv4 required");
		ushort port = reader.ReadUInt16();
		port = (ushort)((port << 8) | (port >> 8));
		
		uint address = reader.ReadUInt32();

		byte[] addressBytes = BitConverter.GetBytes(address);

		return new IPEndPoint(new IPAddress(addressBytes), (port));
	}
}
