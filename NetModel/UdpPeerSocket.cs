using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetModel;

internal class UdpPeerSocket : IDisposable
{
	public const int max_packet_size = 2048;
	internal Socket _socket;

	private bool _disposed = false;

	public UdpPeerSocket()
	{
		_socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
		{
			Blocking = true, // non-blocking causes unity issues maybe
			ExclusiveAddressUse = true,
			DontFragment = true,
		};
	}

	/// <inheritdoc cref="Socket.Bind(EndPoint)"/>
	public void Bind(int port)
	{
		_socket.Bind(new IPEndPoint(IPAddress.Any, port));
	}

	public void BindAny() => Bind(0);

	public void BindRange(int min, int max)
	{
		if (max < min) throw new ArgumentException("Max port must be less than min port");
		if (min < IPEndPoint.MinPort) throw new ArgumentOutOfRangeException(nameof(min));
		if (max > IPEndPoint.MaxPort) throw new ArgumentOutOfRangeException(nameof(max));

		List<Exception> exceptions = [];

		for (int port = min; port <= max; port++)
		{
			try
			{
				Bind(port);
				return;
			} catch (SocketException e) when (e.SocketErrorCode is SocketError.AddressAlreadyInUse)
			{
				exceptions.Add(e);
			}
		}
		throw new AggregateException(exceptions);
	}

	public void Dispose()
	{
		Trace.WriteLine("PeerSocket Disposed");
		_socket.Dispose();
		_disposed = true;
	}

	public IPEndPoint LocalEndPoint => (IPEndPoint)_socket.LocalEndPoint;
	public IPEndPoint RemoteEndPoint => (IPEndPoint)_socket.RemoteEndPoint;

	public void Send(ArraySegment<byte> data)
	{
		_socket.Send(data);
	}

	public async Task SendAsync(ArraySegment<byte> data)
	{
		await _socket.SendAsync(data, SocketFlags.None).ConfigureAwait(false);
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
				await _socket.SendAsync(probe, SocketFlags.None, ct).ConfigureAwait(false);
				await Task.Delay(250, ct).ConfigureAwait(false);
			}
			catch (OperationCanceledException) { break; }
		}
	}

	public async Task HolePunch(Func<byte[]> probe, CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				await _socket.SendAsync(probe(), SocketFlags.None, ct).ConfigureAwait(false);
				await Task.Delay(250, ct).ConfigureAwait(false);
			}
			catch (OperationCanceledException) { break; }
		}
	}

	public async Task StartPolling()
	{
		while (!_disposed) {
			using IMemoryOwner<byte> buffer = MemoryPool<byte>.Shared.Rent(max_packet_size);
			int read;
			try
			{
				read = await Task.Run(() => _socket.Receive(buffer.Memory.Span)).ConfigureAwait(false);
			}
			catch (OperationCanceledException) { break; }
			catch (ObjectDisposedException) { break; }
			catch (SocketException e) when (e.SocketErrorCode is SocketError.Interrupted) { break; }
			catch (SocketException e) when (e.SocketErrorCode is SocketError.MessageSize)
			{
				Trace.Fail(e.Message, $"available: {_socket.Available}");
				throw e;
			}
			catch (Exception e)
			{
				Trace.Fail(e.Message);
				throw e;
			}

			OnFrameReceived?.Invoke(buffer.Memory[..read]);
		}
		Trace.WriteLine("Polling loop exited");
	}

	public event Action<ReadOnlyMemory<byte>>? OnFrameReceived;

	public async Task<IPEndPoint> STUN()
	{
		Socket _socket = this._socket;
		IPAddress stunIP = (await Dns.GetHostAddressesAsync("stun.l.google.com").ConfigureAwait(false)).First(addr => addr.AddressFamily == AddressFamily.InterNetwork);
		IPEndPoint stunEP = new(stunIP, 19302);

		byte[] buffer = new byte[52];
		ArraySegment<byte> sendBuffer = new(buffer, 0, 20);
		ArraySegment<byte> recBuffer = new(buffer, 20, 32);

		const uint MAGIC_COOKIE = 0x2112A442;
		Random rnd = new();

		buffer[1] = 0x01;
		BitConverter.TryWriteBytes(sendBuffer.Slice(4, 4), MAGIC_COOKIE);

		rnd.NextBytes(sendBuffer.Slice(8, 12));

		await _socket.SendToAsync(sendBuffer, SocketFlags.None, stunEP).ConfigureAwait(false);

		int read;
		try
		{
			read = await _socket.ReceiveAsync(recBuffer, SocketFlags.None).ConfigureAwait(false);
		}
		catch (SocketException e) when (e.SocketErrorCode is SocketError.OperationAborted)
		{
			throw new OperationCanceledException("STUN was cancelled", e);
		}

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

	public async Task<IPEndpointMapping> DiscoverMapping()
	{
		IPEndPoint lan = new(await Network.GetPrivateIP(), ((IPEndPoint)_socket.LocalEndPoint).Port);
		IPEndPoint wan = await STUN();
		return new(lan, wan);
	}
}