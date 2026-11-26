using System.Diagnostics;
using System.Text.Json;
using ProstoDesk.UsbRelay.Common;

namespace ProstoDesk.UsbRelay.Host;

internal sealed class UsbipdBackend
{
    private readonly string _exe;

    public UsbipdBackend(string exe = "usbipd") => _exe = exe;

    public async Task<IReadOnlyList<UsbDeviceInfo>> GetDevicesAsync(CancellationToken ct)
    {
        var result = await RunAsync(["state"], ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"usbipd state failed ({result.ExitCode}): {result.StdErr}");

        using var json = JsonDocument.Parse(result.StdOut);
        var devices = new List<UsbDeviceInfo>();
        if (!json.RootElement.TryGetProperty("Devices", out var array) || array.ValueKind != JsonValueKind.Array)
            return devices;

        foreach (var d in array.EnumerateArray())
        {
            string? GetString(string name) => d.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null;
            bool GetBool(string name) => d.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
            Guid? guid = Guid.TryParse(GetString("PersistedGuid"), out var g) ? g : null;
            var instanceId = GetString("InstanceId") ?? string.Empty;
            var (vid, pid) = UsbDeviceInfo.ParseVidPid(instanceId);
            devices.Add(new UsbDeviceInfo(
                GetString("BusId"),
                GetString("Description") ?? "Unknown USB device",
                instanceId,
                GetBool("IsForced"),
                guid,
                GetString("StubInstanceId"),
                GetString("ClientIPAddress"),
                vid,
                pid));
        }
        return devices;
    }

    public Task<ProcessResult> BindAsync(string busId, bool force, CancellationToken ct)
    {
        var args = new List<string> { "bind", "--busid", busId };
        if (force) args.Add("--force");
        return RunAsync(args, ct);
    }

    public Task<ProcessResult> UnbindAsync(string busId, CancellationToken ct) =>
        RunAsync(["unbind", "--busid", busId], ct);

    private async Task<ProcessResult> RunAsync(IEnumerable<string> args, CancellationToken ct)
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

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {_exe}.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
