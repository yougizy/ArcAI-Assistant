using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.Framework;
using ESRI.ArcGIS.SystemUI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArcAI.AddIn
{
    /// <summary>
    /// Bridge to the ArcAI InProc Python Add-In. It writes a job file, waits for
    /// the user to click the ArcAI InProc toolbar button, then reads the result.
    /// Execution happens in ArcMap's own Python 2.7 process and license context.
    /// </summary>
    internal static class InProcRunner
    {
        public static string JobPath
        {
            get { return Path.Combine(AppPaths.Root, "inproc_job.json"); }
        }

        public static string ResultPath
        {
            get { return Path.Combine(AppPaths.Root, "inproc_result.json"); }
        }

        public static void Prepare(string code, JObject context, string outDir)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("代码为空。");
            PythonRunner.EnsureRunner();
            AppPaths.Ensure();
            if (string.IsNullOrEmpty(outDir))
                outDir = ArcMapContext.CreateOutputDir();
            Directory.CreateDirectory(outDir);
            TryDelete(ResultPath);

            var job = new JObject();
            job["code"] = code;
            job["context"] = context ?? new JObject();
            // "CURRENT" makes runner.py use the live document for arcai_export_map.
            job["mxd"] = "CURRENT";
            job["out_dir"] = outDir;
            job["manifest"] = Path.Combine(outDir, "manifest.json");
            File.WriteAllText(JobPath, job.ToString(Formatting.Indented), new UTF8Encoding(false));
        }


        public static bool TryExecuteButton()
        {
            try
            {
                IApplication app = ArcSession.App();
                ICommandBars bars = app.Document.CommandBars;
                string[] ids =
                {
                    "ArcAIInProc_addin.button",
                    "{8f3c2b61-5a7d-4e9c-b2f4-1d6a9c8e7b30}_ArcAIInProc_addin.button",
                    "{8f3c2b61-5a7d-4e9c-b2f4-1d6a9c8e7b30}ArcAIInProc_addin.button",
                    "ArcAI_InProc_Execute"
                };
                foreach (string id in ids)
                {
                    UID uid = new UIDClass();
                    uid.Value = id;
                    ICommandItem item = bars.Find(uid, false, false);
                    if (item != null)
                    {
                        item.Execute();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Could not execute ArcAI InProc button automatically.", ex);
            }
            return false;
        }

        public static async Task<RunnerResult> WaitAsync(int timeoutSeconds,
            CancellationToken token)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(30, timeoutSeconds));
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                if (File.Exists(ResultPath))
                {
                    try
                    {
                        string json = File.ReadAllText(ResultPath, Encoding.UTF8);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            RunnerResult result = Parse(json);
                            TryDelete(ResultPath);
                            return result;
                        }
                    }
                    catch
                    {
                        // The Python Add-In may still be writing; retry.
                    }
                }
                await Task.Delay(350, token).ConfigureAwait(false);
            }
            return new RunnerResult
            {
                Ok = false,
                TimedOut = true,
                Error = "等待 ArcMap 内执行超时。请确认已点击 ArcMap 顶部 ArcAI InProc 工具条的"
                    + "“在 ArcMap 内执行”按钮。"
            };
        }

        private static RunnerResult Parse(string json)
        {
            var result = new RunnerResult();
            result.ResultJson = json;
            JObject root = JObject.Parse(json);
            result.Ok = root["ok"] != null && root["ok"].Type == JTokenType.Boolean
                && (bool)root["ok"];
            result.Stdout = (string)root["stdout"] ?? string.Empty;
            result.Stderr = (string)root["stderr"] ?? string.Empty;
            result.Error = (string)root["error"];
            result.Traceback = (string)root["traceback"];
            double elapsed;
            if (root["elapsed_seconds"] != null
                && double.TryParse(root["elapsed_seconds"].ToString(), out elapsed))
                result.ElapsedSeconds = elapsed;

            JArray layers = root["add_layers"] as JArray;
            if (layers != null)
            {
                foreach (JToken item in layers)
                {
                    result.AddLayers.Add(new LayerResult(
                        (string)item["path"], (string)item["name"]));
                }
            }
            JArray actions = root["map_actions"] as JArray;
            if (actions != null)
            {
                foreach (JToken item in actions)
                {
                    result.MapActions.Add(new MapAction(
                        (string)item["action"], item["args"] as JObject ?? new JObject()));
                }
            }
            JArray images = root["images"] as JArray;
            if (images != null)
            {
                foreach (JToken item in images)
                    result.Images.Add(item.ToString());
            }
            return result;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
