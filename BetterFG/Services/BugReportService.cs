using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;

namespace BetterFG.Services
{
    public static class BugReportService
    {
        private static readonly Regex WinPath = new Regex(@"(?:[A-Za-z]:\\|\\\\)(?:[^\s""'<>|*?\r\n]| (?=[^\\\r\n]{0,40}\\))+");
        private static readonly Regex Ipv4 = new Regex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
        private static readonly Regex Ipv6 = new Regex(@"\b[0-9a-fA-F]{1,4}(?::[0-9a-fA-F]{0,4}){3,7}\b");
        private static readonly Regex GuidRx = new Regex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b");
        private static readonly Regex SteamId = new Regex(@"\b7656119\d{10}\b");
        private static readonly Regex Secret = new Regex(
            @"(?i)\b(token|secret|password|passwd|api[_-]?key|bearer|sessionid)\b\s*[=:]\s*[^\s,;)\]]+");

        private static StreamWriter _writer;
        private static LogLevel _diskLevel = LogLevel.All;
        private static readonly object _writeLock = new object();

        public static string ShareableLogPath => Path.Combine(Paths.BepInExRootPath, "LogOutput.shareable.log");
        public static string ShareableSettingsPath =>
            Path.Combine(Path.GetDirectoryName(SettingsService.SettingsFilePath), "last.shareable.txt");

        public static void Init()
        {
            try
            {
                DiskLogListener disk = null;
                foreach (var listener in BepInEx.Logging.Logger.Listeners)
                    if (listener is DiskLogListener d) { disk = d; break; }
                if (disk != null)
                {
                    _diskLevel = disk.DisplayedLogLevel;
                    disk.LogWriter?.Flush();
                }

                _writer = new StreamWriter(
                    new FileStream(ShareableLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
                    new UTF8Encoding(false));
                _writer.AutoFlush = true;

                string source = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
                if (File.Exists(source))
                {
                    using (var fs = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null) _writer.WriteLine(Scrub(line));
                    }
                }

                BepInEx.Logging.Logger.Listeners.Add(new ScrubListener());
                Plugin.Log.LogInfo($"shareable log now mirrors every line into {ShareableLogPath}");
            }
            catch (Exception ex)
            {
                _writer = null;
                Plugin.Log.LogWarning("couldn't start the shareable log: " + ex.Message);
            }

            ExportSettings();
        }

        public static void Export(bool reveal)
        {
            ExportSettings();

            if (!reveal) return;
            try
            {
                if (File.Exists(ShareableLogPath)) Process.Start("explorer.exe", "/select,\"" + ShareableLogPath + "\"");
                else Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(ShareableSettingsPath) + "\"");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("explorer didn't open: " + ex.Message);
            }
        }

        private static void ExportSettings()
        {
            try
            {
                SettingsService.Flush();
                var lines = new List<string>();
                foreach (string settings in new[] { SettingsService.SettingsFilePath, SettingsService.UserSettingsFilePath })
                    if (File.Exists(settings))
                        foreach (string line in File.ReadAllLines(settings)) lines.Add(Scrub(line));
                File.WriteAllLines(ShareableSettingsPath, lines);
                Plugin.Log.LogInfo($"shareable settings copy, {lines.Count} keys from global + {SettingsService.ActiveUser}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("settings copy failed: " + ex.Message);
            }
        }

        public static string Scrub(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;

            string s = WinPath.Replace(line, m => ScrubPath(m.Value));
            s = Ipv4.Replace(s, m => LooksLikeVersionNumber(m.Value) ? m.Value : "<ip>");
            s = Ipv6.Replace(s, "<ip>");
            s = GuidRx.Replace(s, "<id>");
            s = SteamId.Replace(s, "<steamid>");
            s = Secret.Replace(s, m => m.Groups[1].Value + "=<redacted>");
            return s;
        }

        private static string ScrubPath(string raw)
        {
            string path = raw.TrimEnd('.', ',', ';', ':', ')', ']', '}');
            string trailing = raw.Substring(path.Length);

            int dot = path.LastIndexOf('.');
            string ext = "";
            if (dot > path.LastIndexOf('\\') && path.Length - dot <= 12)
            {
                ext = path.Substring(dot);
                for (int i = 1; i < ext.Length; i++)
                    if (!char.IsLetterOrDigit(ext[i])) { ext = ""; break; }
            }

            return "<path" + ext + ">" + trailing;
        }

        private static bool LooksLikeVersionNumber(string quad)
        {
            int dot = quad.IndexOf('.');
            return int.TryParse(quad.Substring(0, dot), out int first) && first <= 9;
        }

        private sealed class ScrubListener : ILogListener
        {
            public LogLevel LogLevelFilter => LogLevel.All;

            public void LogEvent(object sender, LogEventArgs eventArgs)
            {
                if (_writer == null) return;
                if ((eventArgs.Level & _diskLevel) == 0) return;
                if (DiskLogListener.BlacklistedSources.Contains(eventArgs.Source.SourceName)) return;
                lock (_writeLock)
                {
                    if (_writer == null) return;
                    try { _writer.Write(Scrub(eventArgs.ToStringLine())); }
                    catch { _writer = null; }
                }
            }

            public void Dispose()
            {
                lock (_writeLock)
                {
                    _writer?.Dispose();
                    _writer = null;
                }
            }
        }
    }
}
