using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetModel;
public class P2PSocket : IDisposable
{
	public const int max_packet_size = 1024;
	internal Socket _socket;

	private CancellationTokenSource? pollingCTS;

	public P2PSocket()
	{
		_socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
		{
			Blocking = false,
			ExclusiveAddressUse = true,
			DontFragment = true
		};
	}

	public void Bind(int port)
	{
		_socket.Bind(new IPEndPoint(IPAddress.Any, port));
	}

	public void BindAny() => Bind(0);

	public void Dispose()
	{
		pollingCTS?.Cancel();
		_socket.Dispose();
	}

	public IPEndPoint LocalEndPoint => (IPEndPoint)_socket.LocalEndPoint;
	public IPEndPoint RemoteEndPoint => (IPEndPoint)_socket.RemoteEndPoint;

	public void Send(ArraySegment<byte> data)
	{
		_socket.Send(data);
	}

	public async Task SendAsync(ArraySegment<byte> data)
	{
		await _socket.SendAsync(data, SocketFlags.None);
	}

	public void SetRemote(IPEndPoint ep)
	{
		_socket.Connect(ep);
	}

	public async Task HolePunch(byte[] probe, CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				await _socket.SendAsync(probe, SocketFlags.None, ct);
				await Task.Delay(250, ct);
			} catch (OperationCanceledException) { break; }
		}
	}

	public async Task HolePunch(Func<byte[]> probe, CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				await _socket.SendAsync(probe(), SocketFlags.None, ct);
				await Task.Delay(250, ct);
			}
			catch (OperationCanceledException) { break; }
		}
	}

	public async Task StartPolling(CancellationToken ct = default)
	{
		if (ct == default)
		{
			pollingCTS = new();
			ct = pollingCTS.Token;
		}
		while (!ct.IsCancellationRequested)
		{
			byte[] buffer = new byte[max_packet_size];
			int read;
			try
			{
				read = await _socket.ReceiveAsync(buffer, SocketFlags.None, ct);
			} catch (OperationCanceledException) { break; }
			if (read <= 0) break;
			ArraySegment<byte> segment = new(buffer, 0, read);
			OnMessageRecieved?.Invoke(segment);
		}
	}

	public void EndPolling()
	{
		pollingCTS!.Cancel();
		pollingCTS.Dispose();
		pollingCTS = null;
	}

	public event Action<ArraySegment<byte>>? OnMessageRecieved;

	public async Task<IPEndPoint> STUN()
	{
		Socket _socket = this._socket;
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