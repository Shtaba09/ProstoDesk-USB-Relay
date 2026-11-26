using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ProstoDesk.UsbRelay.Common;

namespace ProstoDesk.UsbRelay.Host
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            return MainAsync(args).GetAwaiter().GetResult();
        }

        private static async Task<int> MainAsync(string[] args)
        {
            using (var cts = new CancellationTokenSource())
            {
                Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                if (args.Length == 0 || IsHelp(args[0]))
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
                            var devices = await backend.GetDevicesAsync(cts.Token).ConfigureAwait(false);
                            Console.WriteLine(new JavaScriptSerializer().Serialize(devices));
                            return 0;

                        case "bind":
                            if (args.Length < 2) return Fail("BUSID is required.");
                            var force = args.Skip(2).Any(x => x.Equals("--force", StringComparison.OrdinalIgnoreCase));
                            var bind = await backend.BindAsync(args[1], force, cts.Token).ConfigureAwait(false);
                            Console.Write(bind.StdOut);
                            Console.Error.Write(bind.StdErr);
                            return bind.ExitCode;

                        case "unbind":
                            if (args.Length < 2) return Fail("BUSID is required.");
                            var unbind = await backend.UnbindAsync(args[1], cts.Token).ConfigureAwait(false);
                            Console.Write(unbind.StdOut);
                            Console.Error.Write(unbind.StdErr);
                            return unbind.ExitCode;

                        case "lab-bridge":
                            return await RunLabBridgeAsync(args.Skip(1).ToArray(), cts.Token).ConfigureAwait(false);

                        default:
                            return Fail("Unknown command: " + args[0]);
                    }
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

        private static async Task<int> RunLabBridgeAsync(string[] args, CancellationToken cancellationToken)
        {
            var listen = GetOption(args, "--listen") ?? "127.0.0.1:3241";
            var target = GetOption(args, "--target") ?? "127.0.0.1:3240";
            var allowLan = args.Any(x => x.Equals("--allow-lan", StringComparison.OrdinalIgnoreCase));
            var listenEp = ParseEndpoint(listen);
            var targetEp = ParseEndpoint(target);

            if (!IPAddress.IsLoopback(listenEp.Address) && !allowLan)
                return Fail("Refusing non-loopback LAB listener without --allow-lan. Never expose this directly to the Internet.");

            var listener = new TcpListener(listenEp);
            var tasks = new List<Task>();
            listener.Start();
            Console.WriteLine("LAB USB/IP bridge: " + listenEp + " -> " + targetEp);
            Console.WriteLine("This mode has no ProstoDesk authentication/encryption. LAN testing only.");

            using (cancellationToken.Register(delegate { try { listener.Stop(); } catch { } }))
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        TcpClient incoming;
                        try
                        {
                            incoming = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
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

                        tasks.Add(RunBridgeConnectionAsync(incoming, targetEp, cancellationToken));
                        tasks.RemoveAll(t => t.IsCompleted);
                    }
                }
                finally
                {
                    try { listener.Stop(); } catch { }
                    try { await Task.WhenAll(tasks.ToArray()).ConfigureAwait(false); } catch { }
                }
            }

            return 0;
        }

        private static async Task RunBridgeConnectionAsync(TcpClient incoming, IPEndPoint target, CancellationToken cancellationToken)
        {
            try
            {
                var upstream = new TcpClient();
                using (cancellationToken.Register(delegate { try { upstream.Close(); } catch { } }))
                {
                    await upstream.ConnectAsync(target.Address, target.Port).ConfigureAwait(false);
                    await StreamPump.RunDuplexAsync(incoming, upstream, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { incoming.Dispose(); }
            catch (SocketException) { incoming.Dispose(); }
            catch (IOException) { incoming.Dispose(); }
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
@"ProstoDesk USB Relay Host (MVP, .NET Framework 4.8)

Commands:
  devices
      Print normalized USB device state from usbipd state as JSON.

  bind <BUSID> [--force]
      Share a device using usbipd-win. Requires Administrator/LocalSystem.

  unbind <BUSID>
      Stop sharing a device.

  lab-bridge [--listen 127.0.0.1:3241] [--target 127.0.0.1:3240] [--allow-lan]
      Raw USB/IP bridge for controlled LAN testing only.
      Production ProstoDesk will replace this direct TCP hop with an authenticated session channel.");
        }
    }
}
