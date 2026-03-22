using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using System.Diagnostics;

namespace NetModel;
public partial class P2PSocket : IDisposable
{
	const int max_packet_size = 1024;
	internal Socket _socket;

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

	public void Dispose()
	{
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

	public async Task HolePunch(CancellationToken ct)
	{
		byte[] probe = "punch"u8.ToArray();

		while (!ct.IsCancellationRequested)
		{
			try
			{
				await _socket.SendAsync(probe, SocketFlags.None, ct);
				await Task.Delay(250, ct);
			} catch (OperationCanceledException) { break; }
		}
	}

	public async Task StartPolling(CancellationToken ct)
	{
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
			OnMessageRecieved?.Invoke(this, new([.. segment]));
		}
	}

	public event EventHandler<MessageRecievedEventArgs>? OnMessageRecieved;

}

public class MessageRecievedEventArgs(byte[] data) : EventArgs()
{
	public byte[] Data { get; } = data;
}