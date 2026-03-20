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

		sock2.OnMessageRecieved += (_, args) => sock2.Send(args.Data.ToArray());
		sock1.OnMessageRecieved += (_, args) => garbageOut.Add([.. args.Data]);

        sock1.Bind(33333);
        sock2.Bind(33334);
        var u1 = sock1.Uplink(new IPEndPoint(IPAddress.Loopback, 33334), 3f);
        var u2 = sock2.Uplink(new IPEndPoint(IPAddress.Loopback, 33333), 3f);

        Assert.IsTrue((await Task.WhenAll(u1, u2)).All(x => x));
        Assert.AreEqual(sock1.LocalEndPoint, sock2.RemoteEndPoint);
        Assert.AreEqual(sock2.LocalEndPoint, sock1.RemoteEndPoint);

        using CancellationTokenSource cts = new();

        var poll = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                sock1.PollEvents(); 
                sock2.PollEvents();
                await Task.Delay(25);
            }
        });

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
    [TestCategory("Manual")]
    [Priority(0)]
    public async Task P2PLoopback()
    {
		using HttpClient client = new();
		string ip = await client.GetStringAsync("https://api.ipify.org");
		Console.WriteLine($"Public IP: {ip}");

        using P2PSocket sock = new();

        sock.OnMessageRecieved += (_, args) => Console.WriteLine(Encoding.UTF8.GetString(args.Data));

        Console.Write("Local port: ");
        if (!int.TryParse(Console.ReadLine(), out int localPort)) Assert.Inconclusive();

        sock.Bind(localPort);

        Console.Write("Remote EP: ");
        if (!IPEndPoint.TryParse(Console.ReadLine() ?? "", out var remoteEP)) Assert.Inconclusive();

        Console.WriteLine($"{ip}:{localPort} ---> {remoteEP}");
        Console.WriteLine("Press enter to begin uplink");
        Console.ReadLine();

        bool success = await sock.Uplink(remoteEP, 10);
        Assert.IsTrue(success);

		using CancellationTokenSource cts = new();

		var poll = Task.Run(async () =>
		{
			while (!cts.IsCancellationRequested)
			{
				sock.PollEvents();
				await Task.Delay(25);
			}
		});

		var io = Task.Run(() =>
		{
			while (!cts.IsCancellationRequested)
            {
                string? s = Console.ReadLine();
                if (string.IsNullOrEmpty(s))
                {
                    cts.Cancel();
                    break;
                }
                sock.Send(Encoding.UTF8.GetBytes(s));
            }
		});

		await Task.WhenAll(poll, io);
	}
}
