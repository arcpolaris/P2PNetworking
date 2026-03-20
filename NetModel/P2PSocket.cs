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
public class P2PSocket : IDisposable
{
	const int max_packet_size = 1024;
	Socket _socket;

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

	public async Task<bool> Uplink(IPEndPoint ep, float timeout)
	{
		using CancellationTokenSource cts = new();
		SocketAsyncEventArgs e = new()
		{
			RemoteEndPoint = ep,
		};
		e.Completed += (_, _) => cts.Cancel();
		_socket.ConnectAsync(e);
		await Task.Delay((int)(1000 * timeout), cts.Token);
		cts.Cancel();
		if (!_socket.Connected)
		{
			Socket.CancelConnectAsync(e);
			return false;
		}

		return true;
	}

	public void Send(ArraySegment<byte> data)
	{
		_socket.Send(data);
	}

	public async Task SendAsync(ArraySegment<byte> data)
	{
		await _socket.SendAsync(data, SocketFlags.None);
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