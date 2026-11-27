using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ProstoDesk.UsbRelay.Common;

namespace ProstoDesk.UsbRelay.Controller
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            return MainAsync(args).GetAwaiter().GetResult();
        }

        private static async Task<int> MainAsync(string[] args)
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintHelp();
                return 0;
            }

            using (var stop = new CancellationTokenSource())
            {
                Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
                {
                    e.Cancel = true;
                    stop.Cancel();
                };

                try
                {
                    if (!args[0].Equals("lab-mount", StringComparison.OrdinalIgnoreCase))
                        return Fail("Unknown command: " + args[0]);
                    if (args.Length < 2) return Fail("BUSID is required.");

                    var busId = args[1];
                    var remoteText = GetOption(args, "--remote");
                    if (string.IsNullOrWhiteSpace(remoteText))
                        throw new ArgumentException("--remote <host:port> is required.");

                    var remote = ParseEndpoint(remoteText);
                    var usbipExe = GetOption(args, "--usbip-exe") ?? "usbip.exe";

                    var listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start();
                    var localPort = ((IPEndPoint)listener.LocalEndpoint).Port;
                    Console.WriteLine("Local USB/IP endpoint: 127.0.0.1:" + localPort + " -> " + remote);

                    var proxyTask = RunProxyAsync(listener, remote, stop.Token);
                    var backend = new UsbipWin2Backend(usbipExe);
                    var vhciPort = await backend.AttachAsync(busId, localPort, stop.Token).ConfigureAwait(false);
                    Console.WriteLine("Mounted BUSID " + busId + " on Windows USB/IP port " + vhciPort + ".");
                    Console.WriteLine("Press Ctrl+C to detach and close the relay.");

                    try { await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }

                    using (var detachTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        try { await backend.DetachAsync(vhciPort, detachTimeout.Token).ConfigureAwait(false); }
                        catch (Exception ex) { Console.Error.WriteLine("Detach warning: " + ex.Message); }
                    }

                    try { listener.Stop(); } catch { }
                    try { await proxyTask.ConfigureAwait(false); } catch { }
                    return 0;
                }
                catch (OperationCanceledException)
                {
                    return 130;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }
        }

        private static async Task RunProxyAsync(TcpListener listener, IPEndPoint remote, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            using (cancellationToken.Register(delegate { try { listener.Stop(); } catch { } }))
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        TcpClient local;
                        try
                        {
                            local = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        }
                        catch (ObjectDisposedException)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            throw;
                        }
                        catch (SocketException)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            throw;
                        }

                        tasks.Add(RunProxyConnectionAsync(local, remote, cancellationToken));
                        tasks.RemoveAll(t => t.IsCompleted);
                    }
                }
                finally
                {
                    try { await Task.WhenAll(tasks.ToArray()).ConfigureAwait(false); } catch { }
                }
            }
        }

        private static async Task RunProxyConnectionAsync(TcpClient local, IPEndPoint remote, CancellationToken cancellationToken)
        {
            try
            {
                var upstream = new TcpClient();
                using (cancellationToken.Register(delegate { try { upstream.Close(); } catch { } }))
                {
                    await upstream.ConnectAsync(remote.Address, remote.Port).ConfigureAwait(false);
                    await StreamPump.RunDuplexAsync(local, upstream, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { local.Dispose(); }
            catch (SocketException) { local.Dispose(); }
            catch (IOException) { local.Dispose(); }
        }

        private static bool IsHelp(string value)
        {
            return string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "help", StringComparison.OrdinalIgnoreCase);
        }

        private static IPEndPoint ParseEndpoint(string value)
        {
            var idx = value.LastIndexOf(':');
            int port;
            if (idx <= 0 || !int.TryParse(value.Substring(idx + 1), out port) || port < 1 || port > 65535)
                throw new ArgumentException("Invalid endpoint: " + value);

            var host = value.Substring(0, idx);
            IPAddress address;
            if (!IPAddress.TryParse(host, out address))
                address = Dns.GetHostAddresses(host).First(a => a.AddressFamily == AddressFamily.InterNetwork);
            return new IPEndPoint(address, port);
        }

        private static string GetOption(string[] args, string name)
        {
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine(message);
            return 2;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(
@"ProstoDesk USB Relay Controller (MVP, .NET Framework 4.8)

Commands:
  lab-mount <BUSID> --remote <client-ip:3241> [--usbip-exe <path>]

Example:
  ProstoDesk.UsbRelay.Controller.exe lab-mount 3-2 --remote 192.168.1.50:3241

The controller creates an ephemeral loopback TCP port, points usbip-win2 at it,
relays the byte stream to the Host bridge, and detaches only its own VHCI port on Ctrl+C.");
        }
    }
}
