using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ArcAI.AddIn
{
    internal sealed class ValidationIssue
    {
        public string Message;
        public int Line;
        public bool Critical;
    }

    /// <summary>
    /// Static checks for common ArcGIS Desktop 10.8 / Python 2.7 mistakes that
    /// otherwise fail only after the script has started. These are heuristics, not
    /// a compiler; they catch the errors that repeatedly break generated code.
    /// </summary>
    internal static class ArcPyValidator
    {
        private sealed class Rule
        {
            public Regex Pattern;
            public string Message;
            public bool Critical;

            public Rule(string pattern, string message, bool critical)
            {
                Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                Message = message;
                Critical = critical;
            }
        }

        private static readonly Rule[] Rules =
        {
            new Rule(@"\barcpy\.mp\b",
                "arcpy.mp 是 ArcGIS Pro API；ArcMap 10.8 请使用 arcpy.mapping", true),
            new Rule(@"MapDocument\s*\(\s*[uUrR]?[""']CURRENT[""']\s*\)",
                "不能使用 MapDocument(\"CURRENT\")；脚本在 ArcMap 外部运行，请使用 arcai_mxd / full_path", true),
            new Rule(@"(^|[^\w])[fF]([""'])",
                "检测到 f-string；Python 2.7 不支持，请使用 .format() 或 % 格式化", true),
            new Rule(@"\b(pathlib|typing|dataclasses|asyncio)\b",
                "检测到 Python 3 专有模块（pathlib/typing/dataclasses/asyncio）", true),
            new Rule(@"\b(async\s+def|await\s+|nonlocal\s+)",
                "检测到 Python 3 的 async/await/nonlocal；Python 2.7 不支持", true),
            new Rule(@"\bdef\s+\w+\s*\([^)]*:\s*[A-Za-z_]",
                "检测到函数类型注解；Python 2.7 不支持", true),
            new Rule(@"\bprint\s*\([^)]*\b(end|sep|file)\s*=",
                "print(..., end=/sep=/file=) 是 Python 3 语法；Python 2.7 不支持", true),
            new Rule(@"\braw_input\s*\(",
                "不要使用 raw_input；脚本在非交互进程中运行", true),
            new Rule(@"\binput\s*\(",
                "不要使用 input；脚本在非交互进程中运行", true),
            new Rule(@"\bwhile\s+True\b",
                "检测到无限循环 while True；请设置明确退出条件", false),
            new Rule(@"arcpy\.env\.overwriteOutput\s*=\s*True",
                "允许覆盖已有输出 overwriteOutput=True；请确认输出路径安全", false),
            new Rule(@"\bClip_management\b",
                "如果输入是栅格，外部进程可能触发 RasterCommander.ImageServer 许可错误；建议改用 arcpy.sa.ExtractByMask", false)
        };

        public static List<ValidationIssue> Validate(string code)
        {
            return Validate(code, false);
        }

        public static List<ValidationIssue> Validate(string code, bool allowCurrent)
        {
            var issues = new List<ValidationIssue>();
            if (string.IsNullOrEmpty(code))
                return issues;

            string[] lines = code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Rule rule in Rules)
                {
                    if (allowCurrent
                        && rule.Pattern.ToString().IndexOf("MapDocument", StringComparison.Ordinal) >= 0)
                        continue;
                    if (!rule.Pattern.IsMatch(lines[i]))
                        continue;
                    string key = rule.Message + "|" + i;
                    if (!seen.Add(key))
                        continue;
                    issues.Add(new ValidationIssue
                    {
                        Message = rule.Message,
                        Line = i + 1,
                        Critical = rule.Critical
                    });
                }
            }
            return issues;
        }

        public static bool HasCritical(IEnumerable<ValidationIssue> issues)
        {
            foreach (ValidationIssue issue in issues)
            {
                if (issue.Critical)
                    return true;
            }
            return false;
        }

        public static string Format(IEnumerable<ValidationIssue> issues)
        {
            var sb = new StringBuilder();
            foreach (ValidationIssue issue in issues)
            {
                sb.AppendLine("第 " + issue.Line + " 行："
                    + (issue.Critical ? "[必须修复] " : "[警告] ") + issue.Message);
            }
            return sb.ToString().TrimEnd();
        }
    }
}
