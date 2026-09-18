using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using BetterFG.Services;

namespace BetterFG.Features.Assets
{
    public static class PythonRuntime
    {
        public static readonly string Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BettrFG", "python");

        public static string Exe => Path.Combine(Root, "python.exe");

        private const string PYTHON_ZIP = "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip";
        private const string GET_PIP = "https://bootstrap.pypa.io/get-pip.py";
        private const string UNITYPY = "UnityPy==1.25.3";

        private static string _python;
        private static string _status;
        private static bool _installing;
        public static float Progress;

        public static string Find()
        {
            if (_python != null) return _python.Length == 0 ? null : _python;
            if (File.Exists(Exe) && Run(Exe, "-c \"import UnityPy\"") == 0)
            {
                _python = Exe;
                Plugin.Log.LogInfo("bundle reader will run on " + Exe);
                return Exe;
            }
            Plugin.Log.LogWarning("no bundle reader under " + Root + " yet");
            _python = "";
            return null;
        }

        public static IEnumerator Ensure(Action<string> status)
        {
            if (Find() != null) yield break;
            if (!_installing)
            {
                _installing = true;
                _status = "asset_export.installing";
                Task.Run(Install);
            }
            string shown = null;
            while (_installing)
            {
                if (_status != shown) { shown = _status; status(shown); }
                yield return null;
            }
            _python = null;
        }

        private static void Install()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
                Directory.CreateDirectory(Root);
                using var http = new HttpClient();

                _status = "asset_export.installing_python";
                Progress = 0f;
                string zipPath = Path.Combine(Root, "python.zip");
                using (var resp = http.GetAsync(PYTHON_ZIP, HttpCompletionOption.ResponseHeadersRead).Result)
                using (var src = resp.Content.ReadAsStreamAsync().Result)
                using (var dst = File.Create(zipPath))
                {
                    long total = resp.Content.Headers.ContentLength ?? 11133606L;
                    var buf = new byte[1 << 16];
                    long got = 0;
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        dst.Write(buf, 0, n);
                        got += n;
                        Progress = (float)got / total;
                    }
                }
                ZipFile.ExtractToDirectory(zipPath, Root);
                File.Delete(zipPath);
                File.WriteAllText(Path.Combine(Root, "python312._pth"),
                    "python312.zip\n.\nLib\\site-packages\n" + FbxExport.Scratch + "\nimport site\n");

                _status = "asset_export.installing_pip";
                Progress = -1f;
                string getPip = Path.Combine(Root, "get-pip.py");
                File.WriteAllBytes(getPip, http.GetByteArrayAsync(GET_PIP).Result);
                if (Run(Exe, "\"" + getPip + "\" --no-warn-script-location") != 0) throw new Exception("get-pip failed");
                File.Delete(getPip);
                if (Run(Exe, "-m pip install --no-warn-script-location setuptools wheel") != 0) throw new Exception("setuptools failed");

                _status = "asset_export.installing_unitypy";
                if (Run(Exe, "-m pip install --no-warn-script-location " + UNITYPY) != 0) throw new Exception("UnityPy failed");

                Plugin.Log.LogInfo("bundle reader installed into " + Root);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("bundle reader install fell over: " + ex.Message);
                try { Directory.Delete(Root, true); } catch { }
            }
            _installing = false;
        }

        private static int Run(string exe, string args)
        {
            try
            {
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                });
                var errTask = p.StandardError.ReadToEndAsync();
                p.StandardOutput.ReadToEnd();
                string err = errTask.Result;
                p.WaitForExit(600000);
                if (!p.HasExited) { p.Kill(); return -1; }
                if (p.ExitCode != 0 && err.Length > 0) Plugin.Log.LogWarning(exe + " " + args + " -> " + err.Trim().Split('\n')[^1].Trim());
                return p.ExitCode;
            }
            catch { return -1; }
        }
    }
}
