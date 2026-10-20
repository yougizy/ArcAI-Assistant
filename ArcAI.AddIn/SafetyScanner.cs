using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ArcAI.AddIn
{
    internal sealed class SafetyWarning
    {
        public string Message;
        public int Line;
        public bool HighRisk;
    }

    /// <summary>
    /// Heuristic pre-flight scan for destructive or unsupported operations. This is
    /// not a sandbox; it only helps the user notice risky generated code.
    /// </summary>
    internal static class SafetyScanner
    {
        private sealed class Rule
        {
            public Regex Pattern;
            public string Message;
            public bool HighRisk;

            public Rule(string pattern, string message, bool highRisk)
            {
                Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                Message = message;
                HighRisk = highRisk;
            }
        }

        private static readonly Rule[] Rules =
        {
            new Rule(@"\bos\.system\s*\(", "调用系统命令 os.system", true),
            new Rule(@"\bsubprocess\b", "启动外部进程 subprocess", true),
            new Rule(@"\bos\.popen\s*\(", "调用系统命令 os.popen", true),
            new Rule(@"\bshutil\.rmtree\s*\(", "递归删除目录 shutil.rmtree", true),
            new Rule(@"\bos\.(remove|unlink)\s*\(", "删除文件 os.remove/os.unlink", true),
            new Rule(@"\barcpy\.Delete_management\s*\(", "删除 GIS 数据 Delete_management", true),
            new Rule(@"\barcpy\.DeleteRows_management\s*\(", "删除要素 DeleteRows_management", true),
            new Rule(@"\barcpy\.TruncateTable_management\s*\(", "清空表 TruncateTable_management", true),
            new Rule(@"\barcpy\.Format\s*\(", "格式化数据 Format", true),
            new Rule(@"\barcpy\.Remove\s*\(", "移除数据 Remove", true),
            new Rule(@"\bwhile\s+True\b", "无限循环 while True", false),
            new Rule(@"\bsys\.exit\s*\(|\bos\._exit\s*\(", "强制退出 Python 进程", true),
            new Rule(@"\b(ctypes|win32api|win32com)\b", "底层 Windows 系统调用", true),
            new Rule(@"\b(requests|urllib2?|socket)\b", "网络访问（脚本不应联网）", false),
            new Rule(@"arcpy\.env\.overwriteOutput\s*=\s*True", "允许覆盖已有输出 overwriteOutput=True", false)
        };

        public static List<SafetyWarning> Scan(string code)
        {
            var warnings = new List<SafetyWarning>();
            if (string.IsNullOrEmpty(code))
                return warnings;

            string[] lines = code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Rule rule in Rules)
                {
                    if (!rule.Pattern.IsMatch(lines[i]))
                        continue;
                    string key = rule.Message + "|" + i;
                    if (seen.Add(key))
                    {
                        warnings.Add(new SafetyWarning
                        {
                            Message = rule.Message,
                            Line = i + 1,
                            HighRisk = rule.HighRisk
                        });
                    }
                }
            }
            return warnings;
        }

        public static bool HasHighRisk(IEnumerable<SafetyWarning> warnings)
        {
            foreach (SafetyWarning warning in warnings)
            {
                if (warning.HighRisk)
                    return true;
            }
            return false;
        }
    }
}
