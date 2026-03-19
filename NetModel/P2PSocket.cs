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

	public void PollEvents()
	{
		while (true)
		{
			byte[] buffer = new byte[max_packet_size];
			try
			{
				int read = _socket.Receive(buffer);
				ArraySegment<byte> segment = new(buffer, 0, read);
				OnMessageRecieved?.Invoke(this, new([.. segment]));
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
			{
				break;
			}
		}
	}

	public event EventHandler<MessageRecievedEventArgs>? OnMessageRecieved;

}

public class MessageRecievedEventArgs(byte[] data) : EventArgs()
{
	public byte[] Data { get; } = data;
}