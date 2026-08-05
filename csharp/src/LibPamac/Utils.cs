using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Pamac
{
    /// <summary>Equivalent of utils.vala.</summary>
    public static class Utils
    {
        public static string? GetOsId()
        {
            var path = "/etc/os-release";
            if (File.Exists(path))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        if (line.StartsWith("ID="))
                        {
                            string[] split = line.Split(new[] { "ID=" }, 2, StringSplitOptions.None);
                            if (split.Length == 2)
                                return split[1];
                        }
                    }
                }
                catch
                {
                    // silent error
                }
            }
            return null;
        }

        public static string GetUserAgent()
        {
            string? id = GetOsId();
            if (id == null)
                return $"Pamac/{Version.Value}";
            return $"Pamac/{Version.Value}_{id}";
        }

        /// <summary>Equivalent of Posix.utsname().machine used for the "auto" architecture.</summary>
        public static string GetMachineArch()
        {
            try
            {
                var psi = new ProcessStartInfo("uname", "-m")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };
                using var p = Process.Start(psi)!;
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                if (output.Length > 0)
                    return output;
            }
            catch
            {
                // fall through
            }
            return RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        }

        /// <summary>Run a shell command and wait for it, like Process.spawn_command_line_sync.</summary>
        public static int RunCommandSync(string command, out string stdout)
        {
            stdout = "";
            try
            {
                var psi = new ProcessStartInfo("/bin/sh", $"-c \"{command.Replace("\"", "\\\"")}\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };
                using var p = Process.Start(psi)!;
                stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode;
            }
            catch
            {
                return -1;
            }
        }
    }
}
