using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;

[assembly: AssemblyTitle("SCwin Setup")]
[assembly: AssemblyDescription("Installateur SCwin")]
[assembly: AssemblyCompany("SoundWin")]
[assembly: AssemblyProduct("SCwin")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

namespace SoundWin.SetupBootstrapper
{
    internal static class Program
    {
        private static readonly byte[] MagicMarker = new byte[] { 0x53, 0x4F, 0x55, 0x4E, 0x44, 0x57, 0x49, 0x4E, 0x53, 0x46, 0x58 }; // "SOUNDWINSFX"

        [STAThread]
        private static int Main(string[] args)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SoundWinSetup_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                string selfPath = Process.GetCurrentProcess().MainModule.FileName;
                Directory.CreateDirectory(tempDir);

                using (var fs = new FileStream(selfPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int footerLength = 8 + MagicMarker.Length;
                    if (fs.Length < footerLength)
                        return 1;

                    fs.Seek(-footerLength, SeekOrigin.End);
                    byte[] footer = new byte[footerLength];
                    int read = fs.Read(footer, 0, footerLength);
                    if (read != footerLength)
                        return 1;

                    for (int i = 0; i < MagicMarker.Length; i++)
                    {
                        if (footer[8 + i] != MagicMarker[i])
                            return 1;
                    }

                    long zipLength = BitConverter.ToInt64(footer, 0);
                    long zipStart = fs.Length - footerLength - zipLength;
                    if (zipStart <= 0 || zipLength <= 0)
                        return 1;

                    string tempZip = Path.Combine(tempDir, "bundle.zip");
                    fs.Seek(zipStart, SeekOrigin.Begin);
                    using (var outZip = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[81920];
                        long remaining = zipLength;
                        while (remaining > 0)
                        {
                            int toRead = (int)Math.Min(buffer.Length, remaining);
                            int n = fs.Read(buffer, 0, toRead);
                            if (n <= 0) break;
                            outZip.Write(buffer, 0, n);
                            remaining -= n;
                        }
                    }

                    ZipFile.ExtractToDirectory(tempZip, tempDir);
                    File.Delete(tempZip);
                }

                string setupExe = Path.Combine(tempDir, "Setup.exe");
                if (!File.Exists(setupExe))
                    return 1;

                var psi = new ProcessStartInfo(setupExe)
                {
                    WorkingDirectory = tempDir,
                    UseShellExecute = true
                };

                var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit();
                }
                return 0;
            }
            catch
            {
                return 1;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { }
            }
        }
    }
}
