using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ProstoDesk.UsbRelay.Controller
{
    internal sealed class UsbipWin2Backend
    {
        private readonly string _exe;

        public UsbipWin2Backend(string exe)
        {
            _exe = string.IsNullOrWhiteSpace(exe) ? "usbip.exe" : exe;
        }

        public UsbipWin2Backend() : this("usbip.exe") { }

        public async Task<int> AttachAsync(string busId, int tcpPort, CancellationToken cancellationToken)
        {
            var result = await RunAsync(new[]
            {
                "--tcp-port", tcpPort.ToString(), "attach", "-r", "127.0.0.1", "-b", busId
            }, cancellationToken).ConfigureAwait(false);

            Console.Write(result.StdOut);
            Console.Error.Write(result.StdErr);
            if (result.ExitCode != 0)
                throw new InvalidOperationException("usbip-win2 attach failed with exit code " + result.ExitCode + ".");

            var match = Regex.Match(result.StdOut + "\n" + result.StdErr, @"attached\s+to\s+port\s+(\d+)", RegexOptions.IgnoreCase);
            int port;
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out port))
                throw new InvalidOperationException("usbip-win2 attached the device but its local VHCI port could not be parsed.");
            return port;
        }

        public async Task DetachAsync(int port, CancellationToken cancellationToken)
        {
            var result = await RunAsync(new[] { "detach", "-p", port.ToString() }, cancellationToken).ConfigureAwait(false);
            Console.Write(result.StdOut);
            Console.Error.Write(result.StdErr);
        }

        private async Task<Result> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken)
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

                return new Result(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
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

        private sealed class Result
        {
            public Result(int exitCode, string stdOut, string stdErr)
            {
                ExitCode = exitCode;
                StdOut = stdOut;
                StdErr = stdErr;
            }

            public int ExitCode { get; private set; }
            public string StdOut { get; private set; }
            public string StdErr { get; private set; }
        }
    }
}
