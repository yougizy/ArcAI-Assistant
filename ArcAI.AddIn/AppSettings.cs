using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace ArcAI.AddIn
{
    internal sealed class ProviderPreset
    {
        public readonly string Name;
        public readonly string BaseUrl;
        public readonly string Model;

        public ProviderPreset(string name, string baseUrl, string model)
        {
            Name = name;
            BaseUrl = baseUrl;
            Model = model;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>Persistent user settings. The API key is stored only through DPAPI.</summary>
    internal sealed class AppSettings
    {
        public string Provider { get; set; }
        public string BaseUrl { get; set; }
        public string Model { get; set; }
        public string ApiKeyProtected { get; set; }
        public bool RememberApiKey { get; set; }
        public double Temperature { get; set; }
        public string ThinkingLevel { get; set; }
        public int TimeoutSeconds { get; set; }
        public int RunTimeoutSeconds { get; set; }
        public int MaxTokens { get; set; }
        public int ContextBudgetTokens { get; set; }
        public bool VerifySsl { get; set; }
        public bool SendContext { get; set; }
        public bool ConfirmBeforeRun { get; set; }
        public bool AutoRunInProc { get; set; }
        public string PythonPath { get; set; }

        [JsonIgnore]
        public string ApiKey { get; set; }

        public static readonly ProviderPreset[] Presets =
        {
            new ProviderPreset("DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat"),
            new ProviderPreset("OpenAI", "https://api.openai.com/v1", "gpt-4o-mini"),
            new ProviderPreset("通义千问", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus"),
            new ProviderPreset("Kimi", "https://api.moonshot.cn/v1", "moonshot-v1-8k"),
            new ProviderPreset("智谱 GLM", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash"),
            new ProviderPreset("本地 Ollama", "http://localhost:11434/v1", "qwen2.5-coder:7b"),
            new ProviderPreset("自定义", "", "")
        };

        public AppSettings()
        {
            Provider = "DeepSeek";
            BaseUrl = "https://api.deepseek.com/v1";
            Model = "deepseek-chat";
            ApiKey = string.Empty;
            ApiKeyProtected = string.Empty;
            RememberApiKey = true;
            Temperature = 0.2;
            ThinkingLevel = "自动";
            TimeoutSeconds = 180;
            RunTimeoutSeconds = 1800;
            MaxTokens = 8192;
            ContextBudgetTokens = 1000000;
            VerifySsl = true;
            SendContext = true;
            ConfirmBeforeRun = true;
            AutoRunInProc = true;
            PythonPath = string.Empty;
        }

        public static AppSettings Load()
        {
            try
            {
                AppPaths.Ensure();
                if (!File.Exists(AppPaths.Settings))
                    return new AppSettings();

                string json = File.ReadAllText(AppPaths.Settings, Encoding.UTF8);
                AppSettings settings = JsonConvert.DeserializeObject<AppSettings>(json);
                if (settings == null)
                    return new AppSettings();

                settings.Normalize();
                if (settings.RememberApiKey)
                    settings.ApiKey = Dpapi.Unprotect(settings.ApiKeyProtected);
                return settings;
            }
            catch (Exception ex)
            {
                Log.Error("Could not load settings; defaults are used.", ex);
                return new AppSettings();
            }
        }

        public void Save()
        {
            Normalize();
            ApiKeyProtected = RememberApiKey && !string.IsNullOrEmpty(ApiKey)
                ? Dpapi.Protect(ApiKey)
                : string.Empty;

            AppPaths.Ensure();
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(AppPaths.Settings, json, new UTF8Encoding(false));
        }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Provider))
                Provider = "DeepSeek";
            if (string.IsNullOrWhiteSpace(BaseUrl))
                BaseUrl = "https://api.deepseek.com/v1";
            if (string.IsNullOrWhiteSpace(Model))
                Model = "deepseek-chat";
            if (Temperature < 0.0)
                Temperature = 0.0;
            if (Temperature > 2.0)
                Temperature = 2.0;
            if (string.IsNullOrWhiteSpace(ThinkingLevel)
                || (ThinkingLevel != "自动" && ThinkingLevel != "低"
                    && ThinkingLevel != "中" && ThinkingLevel != "高"))
                ThinkingLevel = "自动";
            if (TimeoutSeconds < 20)
                TimeoutSeconds = 20;
            if (TimeoutSeconds > 3600)
                TimeoutSeconds = 3600;
            if (RunTimeoutSeconds < 30)
                RunTimeoutSeconds = 30;
            if (RunTimeoutSeconds > 14400)
                RunTimeoutSeconds = 14400;
            if (MaxTokens < 256)
                MaxTokens = 256;
            if (MaxTokens > 32768)
                MaxTokens = 32768;
            if (ContextBudgetTokens < 8000)
                ContextBudgetTokens = 8000;
            if (ContextBudgetTokens > 1000000)
                ContextBudgetTokens = 1000000;
            if (PythonPath == null)
                PythonPath = string.Empty;
        }

        public ProviderPreset FindPreset()
        {
            foreach (ProviderPreset preset in Presets)
            {
                if (string.Equals(preset.Name, Provider, StringComparison.OrdinalIgnoreCase))
                    return preset;
            }
            return Presets[Presets.Length - 1];
        }

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }
    }
}
