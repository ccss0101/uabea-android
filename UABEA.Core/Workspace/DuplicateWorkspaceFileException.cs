using System;

namespace UABEAvalonia
{
    // 当重复加载同一个文件时抛出。
    // 本实现不依赖 AssetsManager 的 FileLookup / GetFileLookupKey
    // （当前引用的 AssetsTools.NET 版本尚未提供这些 API），
    // 仅保留重复文件的关键信息，方便上层提示用户。
    public class DuplicateWorkspaceFileException : Exception
    {
        // 重复文件的路径（或虚拟路径）
        public string FilePath { get; }

        // 如果该文件来自某个 bundle，则记录 bundle 的路径；否则为空字符串
        public string BundlePath { get; }

        public DuplicateWorkspaceFileException(string filePath, string bundlePath = "")
        {
            FilePath = filePath;
            BundlePath = bundlePath ?? string.Empty;
        }

        public override string Message
        {
            get
            {
                if (string.IsNullOrEmpty(BundlePath))
                {
                    return $"文件 '{FilePath}' 已经加载，不能重复加载。";
                }
                else
                {
                    return $"文件 '{FilePath}'（来自 bundle '{BundlePath}'）已经加载，不能重复加载。";
                }
            }
        }
    }
}
