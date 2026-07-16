using AssetsTools.NET;

namespace UABEAvalonia.Mesh
{
    /// <summary>
    /// 描述单个顶点通道的布局信息。参考自 UABEANext4 的 Channel.cs。
    /// 每个 Channel 对应 m_VertexData.m_Channels 数组中的一个元素。
    /// </summary>
    public class Channel
    {
        /// <summary>所属流索引（stream）。不同流在顶点数据中分开存放。</summary>
        public byte stream;
        /// <summary>该通道在一条顶点记录中的字节偏移。</summary>
        public byte offset;
        /// <summary>通道格式（具体含义随 Unity 版本变化，见 VertexChannelFormat / VertexFormatV1 / VertexFormatV2）。</summary>
        public byte format;
        /// <summary>通道维度（低 4 位有效，例如 3 表示 vec3）。</summary>
        public byte dimension;

        public Channel(AssetTypeValueField field)
        {
            stream = field["stream"].AsByte;
            offset = field["offset"].AsByte;
            format = field["format"].AsByte;
            dimension = field["dimension"].AsByte;
        }
    }
}
