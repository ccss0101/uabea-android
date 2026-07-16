namespace UABEAvalonia
{
    /// <summary>
    /// Bundle 内文件类型分类，供 UI 层做颜色映射等展示用。
    /// 与 Avalonia 解耦：核心库只返回类型，颜色由 UI 层 Converter 处理。
    /// </summary>
    public enum BundleItemType
    {
        Serialized,
        Ress,
        Resource,
        Etc
    }
}
