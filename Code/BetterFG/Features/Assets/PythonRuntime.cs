using System;
using System.IO;

namespace BetterFG.Features.Assets
{
    public static class PythonRuntime
    {
        public static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BettrFG", "python");

        public static string Exe => Path.Combine(Root, "python.exe");

        public static bool Ready =>
            File.Exists(Exe) && Directory.Exists(Path.Combine(Root, "Lib", "site-packages", "UnityPy"));
    }
}
