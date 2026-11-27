using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ProstoDesk.UsbRelay.Common
{
    public static class StreamPump
    {
        public static async Task RunDuplexAsync(TcpClient left, TcpClient right, CancellationToken cancellationToken)
        {
            if (left == null) throw new ArgumentNullException("left");
            if (right == null) throw new ArgumentNullException("right");

            using (left)
            using (right)
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var leftStream = left.GetStream())
            using (var rightStream = right.GetStream())
            {
                var leftToRight = PumpDirectionAsync(leftStream, rightStream, right.Client, linked.Token);
                var rightToLeft = PumpDirectionAsync(rightStream, leftStream, left.Client, linked.Token);

                try
                {
                    await Task.WhenAll(leftToRight, rightToLeft).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (!cancellationToken.IsCancellationRequested) throw;
                    linked.Cancel();
                }
                catch (IOException)
                {
                    linked.Cancel();
                }
                catch (SocketException)
                {
                    linked.Cancel();
                }
                finally
                {
                    TryShutdown(left.Client, SocketShutdown.Both);
                    TryShutdown(right.Client, SocketShutdown.Both);
                }
            }
        }

        private static async Task PumpDirectionAsync(
            Stream source,
            Stream destination,
            Socket destinationSocket,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    TryShutdown(destinationSocket, SocketShutdown.Send);
                    return;
                }

                await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            }
        }

        private static void TryShutdown(Socket socket, SocketShutdown how)
        {
            try { socket.Shutdown(how); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
