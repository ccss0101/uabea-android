using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UABEA.Android
{
    /// <summary>
    /// 翻译条目数据模型(已和 AssetTypeValueField 解耦,可序列化到 TXT)。
    /// 一条对应一处可翻译文字。
    /// </summary>
    public class TextEntry
    {
        /// <summary>所属文件相对路径(相对扫描根目录)</summary>
        public string FilePath { get; set; } = "";

        /// <summary>资产 PathID(用于回写时定位)</summary>
        public long PathId { get; set; }

        /// <summary>字段路径(如 m_Name / m_Script / m_LocalizedString[3].text)</summary>
        public string FieldPath { get; set; } = "";

        /// <summary>原始文字</summary>
        public string Original { get; set; } = "";

        /// <summary>翻译后文字(null/空表示未翻译)</summary>
        public string Translated { get; set; } = "";

        /// <summary>是否已修改</summary>
        public bool IsModified => !string.IsNullOrEmpty(Translated) && Translated != Original;

        /// <summary>统一显示名</summary>
        public string DisplayLabel
        {
            get
            {
                string fileName = Path.GetFileName(FilePath);
                string preview = Original.Length > 30 ? Original.Substring(0, 30) + "..." : Original;
                return $"[{fileName}#{PathId}] {FieldPath}\n{preview}";
            }
        }
    }

    /// <summary>
    /// TXT 导入导出格式:
    ///
    /// === FILE::PATHID::FIELD ===
    /// 原文行1
    /// 原文行2
    /// ---
    /// 译文行1
    /// 译文行2
    /// ===
    ///
    /// 多条之间空行分隔。
    /// 字符串里的 ===/--- 会被转义为 \=== \--- 避免冲突。
    /// </summary>
    public static class TextEntryTxtIo
    {
        private const string EntryHeader = "=== FILE::";
        private const string EntrySeparator = "---";
        private const string EntryFooter = "===";

        /// <summary>导出条目到 TXT 字符串</summary>
        public static string Export(List<TextEntry> entries)
        {
            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                sb.Append(EntryHeader);
                sb.Append(e.FilePath).Append("::").Append(e.PathId).Append("::").Append(e.FieldPath);
                sb.AppendLine(" ===");
                sb.AppendLine(Escape(e.Original));
                sb.AppendLine(EntrySeparator);
                sb.AppendLine(Escape(e.Translated));
                sb.AppendLine(EntryFooter);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>导出并写到文件</summary>
        public static void ExportToFile(List<TextEntry> entries, string path)
        {
            File.WriteAllText(path, Export(entries), Encoding.UTF8);
        }

        /// <summary>从 TXT 文本导入,返回 PathId+FieldPath → 译文的映射</summary>
        public static Dictionary<string, string> Import(string txt)
        {
            var result = new Dictionary<string, string>();
            var lines = txt.Replace("\r\n", "\n").Split('\n');

            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i];
                if (line.StartsWith(EntryHeader) && line.TrimEnd().EndsWith(" ==="))
                {
                    // 解析头:=== FILE::path::pathid::field ===
                    string mid = line.Substring(EntryHeader.Length);
                    int endIdx = mid.LastIndexOf(" ===");
                    if (endIdx < 0) { i++; continue; }
                    string header = mid.Substring(0, endIdx);

                    // 找到最后两个 :: 分隔符(因为 field path 里可能含 ::,简化:从右往左找)
                    // header = filepath::pathid::field
                    // filepath 可能含 ::,所以先找最后一个 ::,再从左段找最后一个 ::
                    int lastSep = header.LastIndexOf("::");
                    if (lastSep < 0) { i++; continue; }
                    string fieldPath = header.Substring(lastSep + 2);

                    string beforeField = header.Substring(0, lastSep);
                    int secondLastSep = beforeField.LastIndexOf("::");
                    if (secondLastSep < 0) { i++; continue; }
                    string pathIdStr = beforeField.Substring(secondLastSep + 2);
                    string filePath = beforeField.Substring(0, secondLastSep);

                    if (!long.TryParse(pathIdStr, out long pathId)) { i++; continue; }

                    // 收集原文直到 ---
                    i++;
                    var origLines = new List<string>();
                    while (i < lines.Length && lines[i] != EntrySeparator)
                    {
                        origLines.Add(Unescape(lines[i]));
                        i++;
                    }
                    i++; // 跳过 ---

                    // 收集译文直到 ===
                    var transLines = new List<string>();
                    while (i < lines.Length && lines[i] != EntryFooter)
                    {
                        transLines.Add(Unescape(lines[i]));
                        i++;
                    }

                    string original = string.Join("\n", origLines);
                    string translated = string.Join("\n", transLines);

                    string key = $"{filePath}::{pathId}::{fieldPath}";
                    result[key] = translated;
                }
                i++;
            }
            return result;
        }

        /// <summary>从文件导入</summary>
        public static Dictionary<string, string> ImportFromFile(string path)
        {
            return Import(File.ReadAllText(path, Encoding.UTF8));
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s
                .Replace("\\", "\\\\")
                .Replace("===", "\\=== ")
                .Replace("---", "\\--- ");
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s
                .Replace("\\--- ", "---")
                .Replace("\\=== ", "===")
                .Replace("\\\\", "\\");
        }
    }
}
