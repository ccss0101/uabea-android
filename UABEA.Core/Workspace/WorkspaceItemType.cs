namespace UABEAvalonia
{
    // 工作区中文件项的类型分类。
    // BundleFile   - Unity bundle 文件（可能包含多个内部文件）
    // AssetsFile   - 序列化资产文件（.assets 或 bundle 内的被序列化条目）
    // ResourceFile - 资源文件（.resS / .resource 等二进制资源）
    // OtherFile    - 其它无法归类的文件
    public enum WorkspaceItemType
    {
        BundleFile,
        AssetsFile,
        ResourceFile,
        OtherFile
    }
}
