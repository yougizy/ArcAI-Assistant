using System;
using System.IO;
using System.Text;

namespace ArcAI.AddIn
{
    /// <summary>Well-known per-user paths. Everything lives under %APPDATA%\ArcAI.</summary>
    internal static class AppPaths
    {
        public static string Root
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ArcAI"); }
        }

        public static string Settings { get { return Path.Combine(Root, "settings.json"); } }
        public static string Log { get { return Path.Combine(Root, "arcai.log"); } }
        public static string History { get { return Path.Combine(Root, "history.jsonl"); } }
        public static string Runner { get { return Path.Combine(Root, "runner.py"); } }
        public static string Jobs { get { return Path.Combine(Root, "jobs"); } }
        public static string Outputs { get { return Path.Combine(Root, "outputs"); } }
        public static string Snapshots { get { return Path.Combine(Root, "snapshots"); } }

        public static void Ensure()
        {
            Directory.CreateDirectory(Root);
        }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                AppPaths.Ensure();
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
                lock (Gate)
                {
                    File.AppendAllText(AppPaths.Log, line, new UTF8Encoding(true));
                }
            }
            catch
            {
                // Logging must never take the add-in down.
            }
        }

        public static void Error(string message, Exception ex)
        {
            Write(message + Environment.NewLine + (ex == null ? "(no exception)" : ex.ToString()));
        }
    }
}
