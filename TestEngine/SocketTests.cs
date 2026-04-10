using System.Diagnostics;
using System.Net;
using System.Text;
using NetModel;

namespace TestEngine;

[TestClass]
public sealed class SocketTests
{
    [TestMethod]
    public async Task Loopback()
    {
        List<byte[]> garbageIn = [.. Enumerable.Range(0, 16).Select(i =>
        {
            byte[] bytes = new byte[Random.Shared.Next(1, 1024)];
            bytes[0] = (byte)i;
            Random.Shared.NextBytes((new Span<byte>(bytes))[1..]);
            return bytes;
        })];
        List<byte[]> garbageOut = [];

        using P2PSocket sock1 = new();
        using P2PSocket sock2 = new();

		sock2.OnMessageRecieved += data => sock2.Send(data.ToArray());
		sock1.OnMessageRecieved += data => garbageOut.Add([.. data]);

        sock1.Bind(33333);
        sock2.Bind(33334);
        sock1.SetRemote(new IPEndPoint(IPAddress.Loopback, 33334));
        sock2.SetRemote(new IPEndPoint(IPAddress.Loopback, 33333));

        Assert.AreEqual(sock1.LocalEndPoint, sock2.RemoteEndPoint);
        Assert.AreEqual(sock2.LocalEndPoint, sock1.RemoteEndPoint);

        using CancellationTokenSource cts = new();

        var poll = Task.WhenAll(sock1.StartPolling(cts.Token), sock2.StartPolling(cts.Token));

        var io = Task.Run(async () =>
        {
            foreach (var buff in garbageIn)
            {
                sock1.Send(buff);
                await Task.Delay(50);
            }
            cts.CancelAfter(1000);
        });

        await Task.WhenAll(poll, io);

        Assert.AreEqual(garbageIn.Count, garbageOut.Count);
        foreach (var (First, Second) in garbageIn.OrderBy(buff => buff[0]).Zip(garbageOut.OrderBy(buff => buff[0])))
        {
            Assert.IsTrue(First.SequenceEqual(Second));
        }
    }

    [TestMethod]
    public async Task STUN()
    {
        var ip = await Helpers.GetPublicIP();
        using P2PSocket sock = new();
        sock.Bind(33338);
        var ep = await sock.STUN();

        Debug.WriteLine(ep.ToString());
        Assert.AreEqual(ip, ep.Address);
    }

    [TestMethod]
    [Priority(0)]
    [DataRow(676767, 33335, "67.182.146.214", 33335)]
    //[DataRow(676767, 33335, "174.277.49.79", 33338)]
    public async Task P2PLoopback(int seed, int localPort, string remoteAddr, int remotePort)
    {
        var punch_probe = "punch"u8.ToArray();
		Random rnd = new(seed);
		List<byte[]> garbageIn = [.. Enumerable.Range(0, 16).Select(i =>
		{
			byte[] bytes = new byte[rnd.Next(1, 1024)];
			bytes[0] = (byte)i;
			rnd.NextBytes((new Span<byte>(bytes))[1..]);
			return bytes;
		})];
		List<byte[]> garbageOut = [];

        using P2PSocket sock = new();

        sock.Bind(localPort);
        Debug.WriteLine($"Local port: {localPort}");

        IPEndPoint publicEP = await sock.STUN();
        Debug.WriteLine(publicEP);

        sock.OnMessageRecieved += data =>
        {

			if (data.AsSpan().SequenceEqual(punch_probe))
			{
				Debug.WriteLine("punch got through!!");
				return;
			}
			lock (garbageOut)
            garbageOut.Add(data.ToArray());
        };


        IPEndPoint remoteEP = new(IPAddress.Parse(remoteAddr), remotePort);

        Debug.WriteLine($"Remote EP: {remoteEP}");
        sock.SetRemote(remoteEP);
        Debug.WriteLine($"{publicEP} ---> {remoteEP}");

		using CancellationTokenSource cts = new();
		cts.CancelAfter(TimeSpan.FromSeconds(12));

		var punch = sock.HolePunch(punch_probe, cts.Token);
		var poll = sock.StartPolling(cts.Token);

		var io = Task.Run(async () =>
		{
			await Task.Delay(500);

			foreach (var buff in garbageIn)
			{
				sock.Send(buff);
				await Task.Delay(50);
			}

			await Task.Delay(1000);
			cts.Cancel();
		});

		await Task.WhenAll(punch, poll, io);


		Assert.AreEqual(garbageIn.Count, garbageOut.Count);
		foreach (var (First, Second) in garbageIn.OrderBy(buff => buff[0]).Zip(garbageOut.OrderBy(buff => buff[0])))
		{
			Assert.IsTrue(First.SequenceEqual(Second));
		}
	}
}
