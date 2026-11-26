using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ProstoDesk.UsbRelay.Common;
using ProstoDesk.UsbRelay.Host;

return await MainAsync(args);

static async Task<int> MainAsync(string[] args)
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        PrintHelp();
        return 0;
    }

    var backend = new UsbipdBackend();
    try
    {
        switch (args[0].ToLowerInvariant())
        {
            case "devices":
                var devices = await backend.GetDevicesAsync(cts.Token);
                Console.WriteLine(JsonSerializer.Serialize(devices, new JsonSerializerOptions { WriteIndented = true }));
                return 0;

            case "bind":
                if (args.Length < 2) return Fail("BUSID is required.");
                var force = args.Skip(2).Any(x => x.Equals("--force", StringComparison.OrdinalIgnoreCase));
                var bind = await backend.BindAsync(args[1], force, cts.Token);
                Console.Write(bind.StdOut);
                Console.Error.Write(bind.StdErr);
                return bind.ExitCode;

            case "unbind":
                if (args.Length < 2) return Fail("BUSID is required.");
                var unbind = await backend.UnbindAsync(args[1], cts.Token);
                Console.Write(unbind.StdOut);
                Console.Error.Write(unbind.StdErr);
                return unbind.ExitCode;

            case "lab-bridge":
                return await RunLabBridgeAsync(args.Skip(1).ToArray(), cts.Token);

            default:
                return Fail($"Unknown command: {args[0]}");
        }
    }
    catch (OperationCanceledException) { return 130; }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static async Task<int> RunLabBridgeAsync(string[] args, CancellationToken ct)
{
    var listen = GetOption(args, "--listen") ?? "127.0.0.1:3241";
    var target = GetOption(args, "--target") ?? "127.0.0.1:3240";
    var allowLan = args.Any(x => x == "--allow-lan");
    var listenEp = ParseEndpoint(listen);
    var targetEp = ParseEndpoint(target);

    if (!IPAddress.IsLoopback(listenEp.Address) && !allowLan)
        return Fail("Refusing non-loopback LAB listener without --allow-lan. Never expose this directly to the Internet.");

    var listener = new TcpListener(listenEp);
    listener.Start();
    Console.WriteLine($"LAB USB/IP bridge: {listenEp} -> {targetEp}");
    Console.WriteLine("This mode has no ProstoDesk authentication/encryption. LAN testing only.");

    var tasks = new List<Task>();
    try
    {
        while (!ct.IsCancellationRequested)
        {
            var incoming = await listener.AcceptTcpClientAsync(ct);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var upstream = new TcpClient();
                    await upstream.ConnectAsync(targetEp.Address, targetEp.Port, ct);
                    await StreamPump.RunDuplexAsync(incoming, upstream, ct);
                }
                catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
                {
                    incoming.Dispose();
                }
            }, ct));
            tasks.RemoveAll(t => t.IsCompleted);
        }
    }
    finally
    {
        listener.Stop();
        try { await Task.WhenAll(tasks); } catch { }
    }
    return 0;
}

static IPEndPoint ParseEndpoint(string value)
{
    var idx = value.LastIndexOf(':');
    if (idx <= 0 || !int.TryParse(value[(idx + 1)..], out var port) || port is < 1 or > 65535)
        throw new ArgumentException($"Invalid endpoint: {value}");
    var host = value[..idx];
    if (!IPAddress.TryParse(host, out var address))
        address = Dns.GetHostAddresses(host).First(a => a.AddressFamily == AddressFamily.InterNetwork);
    return new IPEndPoint(address, port);
}

static string? GetOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 2;
}

static void PrintHelp()
{
    Console.WriteLine("""
ProstoDesk USB Relay Host (MVP)

Commands:
  devices
      Print normalized USB device state from `usbipd state` as JSON.

  bind <BUSID> [--force]
      Share a device using usbipd-win. Requires Administrator/LocalSystem.

  unbind <BUSID>
      Stop sharing a device.

  lab-bridge [--listen 127.0.0.1:3241] [--target 127.0.0.1:3240] [--allow-lan]
      Raw USB/IP bridge for controlled LAN testing only.
      Production ProstoDesk will replace this direct TCP hop with an authenticated session channel.
""");
}
