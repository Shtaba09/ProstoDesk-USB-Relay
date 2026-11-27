using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ProstoDesk.UsbRelay.Controller;

internal sealed class UsbipWin2Backend
{
    private readonly string _exe;
    public UsbipWin2Backend(string exe = "usbip.exe") => _exe = exe;

    public async Task<int> AttachAsync(string busId, int tcpPort, CancellationToken ct)
    {
        var r = await RunAsync(["--tcp-port", tcpPort.ToString(), "attach", "-r", "127.0.0.1", "-b", busId], ct);
        Console.Write(r.StdOut);
        Console.Error.Write(r.StdErr);
        if (r.ExitCode != 0) throw new InvalidOperationException($"usbip-win2 attach failed with exit code {r.ExitCode}.");

        var m = Regex.Match(r.StdOut + "\n" + r.StdErr, @"attached\s+to\s+port\s+(\d+)", RegexOptions.IgnoreCase);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var port))
            throw new InvalidOperationException("usbip-win2 attached the device but its local VHCI port could not be parsed.");
        return port;
    }

    public async Task DetachAsync(int port, CancellationToken ct)
    {
        var r = await RunAsync(["detach", "-p", port.ToString()], ct);
        Console.Write(r.StdOut);
        Console.Error.Write(r.StdErr);
    }

    private async Task<Result> RunAsync(IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {_exe}.");
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return new Result(p.ExitCode, await stdout, await stderr);
    }

    private sealed record Result(int ExitCode, string StdOut, string StdErr);
}
