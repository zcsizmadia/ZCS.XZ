using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Test.ZCS.XZ
{
    internal static class XzExecutable
    {
        private static string XzPath;

        static XzExecutable()
        {
            // Resolve the xz executable path based on the current OS and architecture
            string rid = 
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "unknown";

            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => "x64",
            };

            string exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "xz.exe" : "xz";
            XzPath = Path.Combine(AppContext.BaseDirectory, "runtimes", $"{rid}-{arch}", "native", exe);
        }

        public static void Run(string args)
        {
            var psi = new ProcessStartInfo(XzPath, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi)!;
            
            proc.WaitForExit(30_000);
            
            if (proc.ExitCode != 0)
            {
                var stderr = proc.StandardError.ReadToEnd();
                throw new Exception($"xz exited with code {proc.ExitCode}: {stderr}");
            }
        }
    }
}
