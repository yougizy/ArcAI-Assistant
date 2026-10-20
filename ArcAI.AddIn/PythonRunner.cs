using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArcAI.AddIn
{
    internal sealed class MapAction
    {
        public string Action;
        public JObject Args;

        public MapAction(string action, JObject args)
        {
            Action = action;
            Args = args ?? new JObject();
        }
    }

    internal sealed class RunnerResult
    {
        public bool Ok;
        public string Stdout = string.Empty;
        public string Stderr = string.Empty;
        public string Error;
        public string Traceback;
        public List<LayerResult> AddLayers = new List<LayerResult>();
        public List<MapAction> MapActions = new List<MapAction>();
        public List<string> Images = new List<string>();
        public int ExitCode = -1;
        public bool TimedOut;
        public bool Cancelled;
        public double ElapsedSeconds;
        public string ResultJson;
    }

    /// <summary>
    /// Runs AI-generated code in the standalone ArcGIS Python 2.7 interpreter.
    /// The process is fully out-of-process: a crash or loop cannot take ArcMap down,
    /// and the UI stays responsive.
    /// </summary>
    internal static class PythonRunner
    {
        public static string ResolvePython(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return configured;

            string[] candidates =
            {
                @"C:\Python27\ArcGIS10.8\python.exe",
                @"C:\Python27\ArcGIS10.7\python.exe",
                @"C:\Python27\ArcGIS10.6\python.exe",
                @"C:\Python27\ArcGIS10.5\python.exe",
                @"C:\Python27\ArcGIS10.4\python.exe",
                @"C:\Python27\ArcGIS10.3\python.exe",
                @"C:\Python27\ArcGIS10.2\python.exe",
                @"C:\Python27\ArcGIS10.1\python.exe",
                @"C:\Python27\ArcGISx6410.8\python.exe",
                @"C:\Python27\ArcGISx6410.7\python.exe",
                @"C:\Python27\ArcGISx6410.6\python.exe",
                @"C:\Python27\ArcGISx6410.5\python.exe"
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        public static async Task<RunnerResult> RunAsync(string pythonPath, string code,
            JObject context, string mxdSnapshot, string outDir, int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pythonPath) || !File.Exists(pythonPath))
                throw new FileNotFoundException("找不到 ArcGIS Python 2.7。请在设置中指定 python.exe 路径。", pythonPath);
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("代码为空。");

            EnsureRunner();
            AppPaths.Ensure();
            Directory.CreateDirectory(AppPaths.Jobs);
            if (string.IsNullOrEmpty(outDir))
                outDir = ArcMapContext.CreateOutputDir();
            Directory.CreateDirectory(outDir);

            string jobDir = Path.Combine(AppPaths.Jobs, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(jobDir);
            string jobPath = Path.Combine(jobDir, "job.json");
            string resultPath = Path.Combine(jobDir, "result.json");
            string manifestPath = Path.Combine(jobDir, "manifest.json");

            var job = new JObject();
            job["code"] = code;
            job["context"] = context ?? new JObject();
            job["mxd"] = mxdSnapshot == null ? JValue.CreateNull() : new JValue(mxdSnapshot);
            job["out_dir"] = outDir;
            job["manifest"] = manifestPath;
            File.WriteAllText(jobPath, job.ToString(Formatting.Indented), new UTF8Encoding(false));

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var stdoutGate = new object();
            var stderrGate = new object();
            var result = new RunnerResult();
            int timeoutMs = Math.Max(30, timeoutSeconds) * 1000;

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = Quote(AppPaths.Runner) + " " + Quote(jobPath) + " " + Quote(resultPath),
                WorkingDirectory = outDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };
            try
            {
                startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                startInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
            }
            catch
            {
            }

            using (var process = new Process())
            {
                process.StartInfo = startInfo;
                process.EnableRaisingEvents = true;
                var exited = new TaskCompletionSource<bool>();
                process.Exited += delegate
                {
                    try { exited.TrySetResult(true); } catch { }
                };
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    if (args.Data == null)
                        return;
                    lock (stdoutGate) stdout.AppendLine(args.Data);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
                {
                    if (args.Data == null)
                        return;
                    lock (stderrGate) stderr.AppendLine(args.Data);
                };

                if (!process.Start())
                    throw new InvalidOperationException("无法启动 Python 2.7 进程。");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (process.HasExited)
                    exited.TrySetResult(true);

                using (cancellationToken.Register(delegate { KillProcessTree(process); }))
                {
                    Task completed = await Task.WhenAny(exited.Task, Task.Delay(timeoutMs))
                        .ConfigureAwait(false);
                    if (completed != exited.Task)
                    {
                        KillProcessTree(process);
                        result.TimedOut = !cancellationToken.IsCancellationRequested;
                        result.Cancelled = cancellationToken.IsCancellationRequested;
                        result.Error = result.Cancelled
                            ? "执行已取消。"
                            : "Python 执行超时（" + Math.Max(30, timeoutSeconds) + " 秒），已终止进程。";
                        lock (stdoutGate) result.Stdout = stdout.ToString();
                        lock (stderrGate) result.Stderr = stderr.ToString();
                        return result;
                    }
                }

                try { process.WaitForExit(); } catch { }
                try { result.ExitCode = process.ExitCode; } catch { }
            }

            lock (stdoutGate) result.Stdout = stdout.ToString();
            lock (stderrGate) result.Stderr = stderr.ToString();

            if (cancellationToken.IsCancellationRequested)
            {
                result.Ok = false;
                result.Cancelled = true;
                result.Error = "执行已取消。";
                TryDeleteJobDir(jobDir, true);
                return result;
            }

            if (!File.Exists(resultPath))
            {
                result.Ok = false;
                result.Error = "Python runner 没有生成 result.json（退出码 " + result.ExitCode + "）。";
                return result;
            }

            try
            {
                string json = File.ReadAllText(resultPath, Encoding.UTF8);
                result.ResultJson = json;
                JObject written = JObject.Parse(json);
                result.Ok = written["ok"] != null && written["ok"].Value<bool>();
                string capturedOut = written["stdout"] == null ? string.Empty : written["stdout"].ToString();
                string capturedErr = written["stderr"] == null ? string.Empty : written["stderr"].ToString();
                if (!string.IsNullOrEmpty(capturedOut))
                    result.Stdout = capturedOut.TrimEnd();
                if (!string.IsNullOrEmpty(capturedErr))
                    result.Stderr = capturedErr.TrimEnd();
                result.Error = written["error"] == null ? null : written["error"].ToString();
                result.Traceback = written["traceback"] == null ? null : written["traceback"].ToString();
                if (written["elapsed_seconds"] != null)
                    double.TryParse(written["elapsed_seconds"].ToString(), out result.ElapsedSeconds);

                JArray layers = written["add_layers"] as JArray;
                if (layers != null)
                {
                    foreach (JToken item in layers)
                    {
                        if (item == null)
                            continue;
                        string path = item["path"] == null ? null : item["path"].ToString();
                        string name = item["name"] == null ? null : item["name"].ToString();
                        if (!string.IsNullOrWhiteSpace(path))
                            result.AddLayers.Add(new LayerResult(path, name));
                    }
                }

                JArray actions = written["map_actions"] as JArray;
                if (actions != null)
                {
                    foreach (JToken item in actions)
                    {
                        if (item == null)
                            continue;
                        string action = item["action"] == null ? null : item["action"].ToString();
                        JObject args = item["args"] as JObject;
                        if (!string.IsNullOrWhiteSpace(action))
                            result.MapActions.Add(new MapAction(action, args));
                    }
                }

                JArray images = written["images"] as JArray;
                if (images != null)
                {
                    foreach (JToken item in images)
                    {
                        if (item == null)
                            continue;
                        string path = item.ToString();
                        if (!string.IsNullOrWhiteSpace(path))
                            result.Images.Add(path);
                    }
                }
                if (!result.Ok && string.IsNullOrWhiteSpace(result.Error))
                    result.Error = "Python 脚本执行失败。";
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = "无法解析 Python runner 结果：" + ex.Message;
                result.Traceback = ex.ToString();
                Log.Error("Could not parse runner result.", ex);
            }

            TryDeleteJobDir(jobDir, result.Ok);
            return result;
        }

        internal static void EnsureRunner()
        {
            AppPaths.Ensure();
            Assembly assembly = Assembly.GetExecutingAssembly();
            Stream stream = assembly.GetManifestResourceStream("ArcAI.AddIn.Python.runner.py");
            if (stream == null)
            {
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith("runner.py", StringComparison.OrdinalIgnoreCase))
                    {
                        stream = assembly.GetManifestResourceStream(name);
                        break;
                    }
                }
            }
            if (stream == null)
                throw new InvalidOperationException("嵌入的 Python runner.py 资源缺失。");

            using (stream)
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string text = reader.ReadToEnd();
                File.WriteAllText(AppPaths.Runner, text, new UTF8Encoding(false));
            }
        }

        private static void KillProcessTree(Process process)
        {
            if (process == null)
                return;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "taskkill.exe"),
                    Arguments = "/PID " + process.Id + " /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process taskkill = Process.Start(startInfo))
                {
                    if (taskkill != null)
                        taskkill.WaitForExit(5000);
                }
            }
            catch
            {
            }
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch
            {
            }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static void TryDeleteJobDir(string jobDir, bool ok)
        {
            if (!ok)
                return; // keep failed jobs for diagnosis
            try
            {
                if (Directory.Exists(jobDir))
                    Directory.Delete(jobDir, true);
            }
            catch
            {
            }
        }
    }
}
