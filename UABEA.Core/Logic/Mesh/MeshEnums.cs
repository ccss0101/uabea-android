namespace UABEAvalonia.Mesh
{
    // 顶点通道相关枚举。参考自 UABEANext4 的 MeshEnums.cs，
    // 对应 AssetStudio 的 Mesh 解析逻辑，覆盖 Unity 三套版本格式。

    /// <summary>Unity 5 及更早版本的顶点通道格式。</summary>
    public enum VertexChannelFormat
    {
        Float,
        Float16,
        Color,
        Byte,
        UInt32
    }

    /// <summary>Unity 2017 / 2018 使用的顶点格式（V1）。</summary>
    public enum VertexFormatV1
    {
        Float,
        Float16,
        Color,
        UNorm8,
        SNorm8,
        UNorm16,
        SNorm16,
        UInt8,
        SInt8,
        UInt16,
        SInt16,
        UInt32,
        SInt32
    }

    /// <summary>Unity 2018+ 使用的顶点格式（V2，当前主流）。</summary>
    public enum VertexFormatV2
    {
        Float,
        Float16,
        UNorm8,
        SNorm8,
        UNorm16,
        SNorm16,
        UInt8,
        SInt8,
        UInt16,
        SInt16,
        UInt32,
        SInt32
    }

    /// <summary>Unity 2018+ 的通道类型（按通道索引推断语义）。</summary>
    public enum ChannelTypeV3
    {
        Vertex,
        Normal,
        Tangent,
        Color,
        TexCoord0,
        TexCoord1,
        TexCoord2,
        TexCoord3,
        TexCoord4,
        TexCoord5,
        TexCoord6,
        TexCoord7,
        BlendWeight,
        BlendIndices,
    }

    /// <summary>Unity 5 ~ 2017 的通道类型。</summary>
    public enum ChannelTypeV2
    {
        Vertex,
        Normal,
        Color,
        TexCoord0,
        TexCoord1,
        TexCoord2,
        TexCoord3,
        Tangent,
    }

    /// <summary>更早期版本的通道类型。</summary>
    public enum ChannelTypeV1
    {
        Vertex,
        Normal,
        Color,
        TexCoord0,
        TexCoord1,
        Tangent,
    }
}
