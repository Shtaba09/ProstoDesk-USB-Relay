using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ProstoDesk.UsbRelay.Common;

namespace ProstoDesk.UsbRelay.Host
{
    internal sealed class UsbipdBackend
    {
        private readonly string _exe;

        public UsbipdBackend(string exe)
        {
            _exe = string.IsNullOrWhiteSpace(exe) ? "usbipd" : exe;
        }

        public UsbipdBackend() : this("usbipd") { }

        public async Task<IReadOnlyList<UsbDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken)
        {
            var result = await RunAsync(new[] { "state" }, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(string.Format("usbipd state failed ({0}): {1}", result.ExitCode, result.StdErr));

            var serializer = new JavaScriptSerializer();
            var state = serializer.Deserialize<UsbipdState>(result.StdOut) ?? new UsbipdState();
            var devices = new List<UsbDeviceInfo>();

            foreach (var item in state.Devices ?? new UsbipdStateDevice[0])
            {
                Guid parsedGuid;
                Guid? guid = Guid.TryParse(item.PersistedGuid, out parsedGuid) ? parsedGuid : (Guid?)null;
                string vid;
                string pid;
                UsbDeviceInfo.ParseVidPid(item.InstanceId, out vid, out pid);

                devices.Add(new UsbDeviceInfo(
                    item.BusId,
                    string.IsNullOrEmpty(item.Description) ? "Unknown USB device" : item.Description,
                    item.InstanceId ?? string.Empty,
                    item.IsForced,
                    guid,
                    item.StubInstanceId,
                    item.ClientIPAddress,
                    vid,
                    pid));
            }

            return devices;
        }

        public Task<ProcessResult> BindAsync(string busId, bool force, CancellationToken cancellationToken)
        {
            var args = new List<string> { "bind", "--busid", busId };
            if (force) args.Add("--force");
            return RunAsync(args, cancellationToken);
        }

        public Task<ProcessResult> UnbindAsync(string busId, CancellationToken cancellationToken)
        {
            return RunAsync(new[] { "unbind", "--busid", busId }, cancellationToken);
        }

        private async Task<ProcessResult> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _exe,
                Arguments = BuildArguments(args),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = new Process { StartInfo = psi })
            {
                if (!process.Start())
                    throw new InvalidOperationException("Could not start " + _exe + ".");

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                using (cancellationToken.Register(delegate
                {
                    try { if (!process.HasExited) process.Kill(); }
                    catch (InvalidOperationException) { }
                }))
                {
                    await Task.Run(delegate { process.WaitForExit(); }, cancellationToken).ConfigureAwait(false);
                }

                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);
                return new ProcessResult(process.ExitCode, stdout, stderr);
            }
        }

        private static string BuildArguments(IEnumerable<string> args)
        {
            var result = new List<string>();
            foreach (var arg in args)
                result.Add(QuoteArgument(arg));
            return string.Join(" ", result.ToArray());
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            if (value.IndexOfAny(new[] { ' ', '\t', '\"' }) < 0) return value;
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        internal sealed class ProcessResult
        {
            public ProcessResult(int exitCode, string stdOut, string stdErr)
            {
                ExitCode = exitCode;
                StdOut = stdOut;
                StdErr = stdErr;
            }

            public int ExitCode { get; private set; }
            public string StdOut { get; private set; }
            public string StdErr { get; private set; }
        }

        private sealed class UsbipdState
        {
            public UsbipdStateDevice[] Devices { get; set; }
        }

        private sealed class UsbipdStateDevice
        {
            public string BusId { get; set; }
            public string Description { get; set; }
            public string InstanceId { get; set; }
            public bool IsForced { get; set; }
            public string PersistedGuid { get; set; }
            public string StubInstanceId { get; set; }
            public string ClientIPAddress { get; set; }
        }
    }
}
