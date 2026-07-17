using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Collections.Generic;

namespace UABEA.Android
{
    /// <summary>
    /// 递归遍历 AssetTypeValueField,收集所有字符串字段。
    /// 用于:TextAsset 的 m_Script / MonoBehaviour 里的字符串字段。
    /// 同时记录字段路径,便于回写时定位。
    /// </summary>
    public class StringExtractor
    {
        /// <summary>收集到的字符串条目</summary>
        public List<ExtractedString> Strings { get; } = new();

        /// <summary>
        /// 从 baseField 开始递归收集所有字符串字段。
        /// </summary>
        /// <param name="baseField">资产根字段(由 am.GetBaseField 取得)</param>
        /// <param name="rootPath">路径前缀(通常为空字符串)</param>
        public void Extract(AssetTypeValueField baseField, string rootPath = "")
        {
            if (baseField == null) return;
            ExtractInternal(baseField, rootPath);
        }

        private void ExtractInternal(AssetTypeValueField field, string path)
        {
            if (field == null) return;

            // 字符串字段:直接收集
            if (field.Value != null && field.Value.ValueType == AssetValueType.String)
            {
                string value = field.AsString ?? "";
                // 跳过空字符串(翻译无意义)
                if (!string.IsNullOrEmpty(value))
                {
                    Strings.Add(new ExtractedString
                    {
                        Path = path,
                        Original = value,
                        FieldRef = field
                    });
                }
                return; // 字符串字段无子字段
            }

            // ManagedReferencesRegistry(SerializeReference):展开 references.data 递归
            if (field.Value != null && field.Value.ValueType == AssetValueType.ManagedReferencesRegistry)
            {
                var registry = field.AsManagedReferencesRegistry;
                if (registry?.references != null)
                {
                    for (int i = 0; i < registry.references.Count; i++)
                    {
                        ExtractInternal(registry.references[i].data, $"{path}[{i}].");
                    }
                }
                return;
            }

            // 数组:遍历每个元素
            var template = field.TemplateField;
            bool isArray = template != null && template.IsArray;
            var children = field.Children;
            if (children == null || children.Count == 0) return;

            if (isArray)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    ExtractInternal(children[i], $"{path}[{i}]");
                }
            }
            else
            {
                // 普通结构:每个子字段用字段名作为路径
                foreach (var child in children)
                {
                    if (child == null) continue;
                    string childName = child.TemplateField?.Name ?? "?";
                    string childPath = string.IsNullOrEmpty(path) ? childName : $"{path}.{childName}";
                    ExtractInternal(child, childPath);
                }
            }
        }

        /// <summary>
        /// 专门提取 TextAsset 的 m_Script 字段(大文本)。
        /// TextAsset 的 m_Script 是 TypelessData(ByteArray),不是普通字符串。
        /// </summary>
        public static ExtractedString? ExtractTextAsset(AssetTypeValueField baseField)
        {
            if (baseField == null) return null;
            var scriptField = baseField["m_Script"];
            if (scriptField == null) return null;

            byte[]? bytes = scriptField.AsByteArray;
            if (bytes == null || bytes.Length == 0) return null;

            string text;
            try
            {
                text = System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                text = System.Text.Encoding.Latin1.GetString(bytes);
            }

            return new ExtractedString
            {
                Path = "m_Script",
                Original = text,
                FieldRef = scriptField,
                IsTextAssetScript = true,
                OriginalBytes = bytes
            };
        }
    }

    /// <summary>
    /// 单个提取出的字符串条目。
    /// FieldRef 用于回写:翻译后直接 field.AsString = newValue 或 field.AsByteArray = bytes。
    /// </summary>
    public class ExtractedString
    {
        /// <summary>字段路径(如 m_Name / m_LocalizedString[3].text)</summary>
        public string Path { get; set; } = "";

        /// <summary>原始字符串</summary>
        public string Original { get; set; } = "";

        /// <summary>翻译后的字符串(用户编辑后赋值;null 表示未修改)</summary>
        public string? Translated { get; set; }

        /// <summary>字段引用(用于回写)</summary>
        public AssetTypeValueField? FieldRef { get; set; }

        /// <summary>是否为 TextAsset 的 m_Script(需要按字节写回)</summary>
        public bool IsTextAssetScript { get; set; }

        /// <summary>原始字节(TextAsset 专用)</summary>
        public byte[]? OriginalBytes { get; set; }

        /// <summary>是否被修改过</summary>
        public bool IsModified => Translated != null && Translated != Original;
    }
}
