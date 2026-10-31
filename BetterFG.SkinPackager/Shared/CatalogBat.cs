using System.Diagnostics;
using System.IO;

namespace BetterFG.Editor
{
    public static class CatalogBat
    {
        public static void Run(string repoRoot)
        {
            string bat = Path.Combine(repoRoot, "generate_catalog.bat");
            if (!File.Exists(bat)) return;

            var psi = new ProcessStartInfo
            {
                FileName = bat,
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var p = Process.Start(psi))
                p.WaitForExit();
        }
    }
}
