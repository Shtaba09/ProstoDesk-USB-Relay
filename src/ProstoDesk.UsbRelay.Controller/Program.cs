using System.Net;
using System.Net.Sockets;
using ProstoDesk.UsbRelay.Common;
using ProstoDesk.UsbRelay.Controller;

return await MainAsync(args);

static async Task<int> MainAsync(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        PrintHelp();
        return 0;
    }

    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

    try
    {
        if (!args[0].Equals("lab-mount", StringComparison.OrdinalIgnoreCase))
            return Fail($"Unknown command: {args[0]}");
        if (args.Length < 2) return Fail("BUSID is required.");

        var busId = args[1];
        var remoteText = GetOption(args, "--remote") ?? throw new ArgumentException("--remote <host:port> is required.");
        var remote = ParseEndpoint(remoteText);
        var usbipExe = GetOption(args, "--usbip-exe") ?? "usbip.exe";

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var localPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        Console.WriteLine($"Local USB/IP endpoint: 127.0.0.1:{localPort} -> {remote}");

        var proxyTask = RunProxyAsync(listener, remote, stop.Token);
        var backend = new UsbipWin2Backend(usbipExe);
        var vhciPort = await backend.AttachAsync(busId, localPort, stop.Token);
        Console.WriteLine($"Mounted BUSID {busId} on Windows USB/IP port {vhciPort}.");
        Console.WriteLine("Press Ctrl+C to detach and close the relay.");

        try { await Task.Delay(Timeout.Infinite, stop.Token); }
        catch (OperationCanceledException) { }

        using var detachTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await backend.DetachAsync(vhciPort, detachTimeout.Token); }
        catch (Exception ex) { Console.Error.WriteLine($"Detach warning: {ex.Message}"); }

        listener.Stop();
        try { await proxyTask; } catch { }
        return 0;
    }
    catch (OperationCanceledException) { return 130; }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static async Task RunProxyAsync(TcpListener listener, IPEndPoint remote, CancellationToken ct)
{
    var tasks = new List<Task>();
    try
    {
        while (!ct.IsCancellationRequested)
        {
            var local = await listener.AcceptTcpClientAsync(ct);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var upstream = new TcpClient();
                    await upstream.ConnectAsync(remote.Address, remote.Port, ct);
                    await StreamPump.RunDuplexAsync(local, upstream, ct);
                }
                catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
                {
                    local.Dispose();
                }
            }, ct));
            tasks.RemoveAll(t => t.IsCompleted);
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    catch (ObjectDisposedException) when (ct.IsCancellationRequested) { }
    finally
    {
        try { await Task.WhenAll(tasks); } catch { }
    }
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
ProstoDesk USB Relay Controller (MVP)

Commands:
  lab-mount <BUSID> --remote <client-ip:3241> [--usbip-exe <path>]

Example:
  ProstoDesk.UsbRelay.Controller.exe lab-mount 3-2 --remote 192.168.1.50:3241

The controller creates an ephemeral loopback TCP port, points usbip-win2 at it,
relays the byte stream to the Host bridge, and detaches only its own VHCI port on Ctrl+C.
""");
}
