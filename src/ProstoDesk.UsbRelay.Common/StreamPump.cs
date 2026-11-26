using System.Net.Sockets;

namespace ProstoDesk.UsbRelay.Common;

public static class StreamPump
{
    public static async Task RunDuplexAsync(TcpClient left, TcpClient right, CancellationToken cancellationToken)
    {
        using (left)
        using (right)
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            using var leftStream = left.GetStream();
            using var rightStream = right.GetStream();

            var leftToRight = PumpDirectionAsync(leftStream, rightStream, right.Client, linked.Token);
            var rightToLeft = PumpDirectionAsync(rightStream, leftStream, left.Client, linked.Token);

            try
            {
                await Task.WhenAll(leftToRight, rightToLeft).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                linked.Cancel();
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                linked.Cancel();
            }
            finally
            {
                try { left.Client.Shutdown(SocketShutdown.Both); } catch (SocketException) { }
                try { right.Client.Shutdown(SocketShutdown.Both); } catch (SocketException) { }
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
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                try { destinationSocket.Shutdown(SocketShutdown.Send); } catch (SocketException) { }
                return;
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}
