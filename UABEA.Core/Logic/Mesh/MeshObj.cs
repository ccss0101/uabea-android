using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UABEAvalonia.Mesh
{
    /// <summary>
    /// 从 Mesh 资产的 <see cref="AssetTypeValueField"/> 解析出的可渲染网格数据。
    /// 参考自 UABEANext4 的 MeshObj.cs（基于 AssetStudio 的 Mesh.cs）。
    ///
    /// 放在平台无关的 UABEA.Core 中，便于 MeshPlugin（独立插件）与
    /// MeshPreviewerControl（桌面端 OpenGL 控件）共用同一类型。
    /// 只提取渲染所需的 Position / Normal / Indices，其它通道（UV、颜色等）按需保留。
    /// </summary>
    public class MeshObj
    {
        /// <summary>索引数组（每 3 个构成一个三角形）。</summary>
        public ushort[] Indices;
        /// <summary>原始通道布局列表（按 m_Channels 顺序）。</summary>
        public List<Channel> Channels;
        /// <summary>顶点位置数据，按 [x0,y0,z0, x1,y1,z1, ...] 平铺。</summary>
        public float[] Vertices;
        /// <summary>顶点法线数据，平铺方式同 <see cref="Vertices"/>（可能为空）。</summary>
        public float[] Normals;
        /// <summary>顶点切线数据（保留，预览暂不使用）。</summary>
        public float[] Tangents;
        /// <summary>顶点颜色数据（保留）。</summary>
        public float[] Colors;
        /// <summary>各层 UV 数据（保留）。</summary>
        public float[][] UVs;

        public MeshObj()
        {
            Indices = [];
            Channels = [];
            Vertices = [];
            Normals = [];
            Tangents = [];
            Colors = [];
            UVs = [];
        }

        /// <summary>
        /// 从 Mesh 资产的基础字段构造。需要 <see cref="AssetsFileInstance"/> 以处理
        /// archive:/ 与 resS 流式数据（m_StreamData）。
        /// </summary>
        public MeshObj(AssetsFileInstance fileInst, AssetTypeValueField baseField, UnityVersion version)
        {
            Indices = [];
            Channels = [];
            Vertices = [];
            Normals = [];
            Tangents = [];
            Colors = [];
            UVs = [];

            Read(fileInst, baseField, version);
        }

        /// <summary>
        /// 便捷静态工厂：从基础字段与所在文件实例解析 MeshObj，
        /// Unity 版本取自文件元数据。供 MeshPlugin 调用。
        /// </summary>
        public static MeshObj FromBaseField(AssetTypeValueField baseField, AssetsFileInstance fileInst)
        {
            var version = new UnityVersion(fileInst.file.Metadata.UnityVersion);
            return new MeshObj(fileInst, baseField, version);
        }

        private void Read(AssetsFileInstance fileInst, AssetTypeValueField baseField, UnityVersion version)
        {
            ReadIndicesData(baseField);
            ReadChannels(baseField);
            ReadVertexData(fileInst, baseField, version);
        }

        // 读取 m_IndexBuffer（小端字节序，按 ushort 解析）
        private void ReadIndicesData(AssetTypeValueField baseField)
        {
            var indicesField = baseField["m_IndexBuffer.Array"].AsByteArray;
            var ushortArray = new ushort[indicesField.Length / 2];
            for (var i = 0; i < indicesField.Length; i += 2)
            {
                ushortArray[i / 2] = (ushort)(indicesField[i + 1] << 8 | indicesField[i]);
            }
            Indices = ushortArray;
        }

        // 读取 m_VertexData.m_Channels，构造 Channel 列表
        private void ReadChannels(AssetTypeValueField baseField)
        {
            var channelFields = baseField["m_VertexData"]["m_Channels.Array"];
            var channels = new List<Channel>();
            foreach (var channelField in channelFields)
            {
                channels.Add(new Channel(channelField));
            }
            Channels = channels;
        }

        // 计算每个 stream 中一条顶点记录的字节长度（取该 stream 下所有通道的结尾偏移最大值）
        private List<int> GetStreamLengths(UnityVersion version)
        {
            var streamLengths = new List<int>();
            var streamCount = Channels.Max(c => c.stream) + 1;
            for (var i = 0; i < streamCount; i++)
            {
                var maxEndOffset = 0;
                for (var j = 0; j < Channels.Count; j++)
                {
                    if (Channels[j].stream == i)
                    {
                        var channel = Channels[j];
                        var size = GetFormatSize(ToVertexFormatV2(channel.format, version));
                        var endOffset = channel.offset + (channel.dimension & 0xf) * size;
                        maxEndOffset = endOffset > maxEndOffset ? endOffset : maxEndOffset;
                    }
                }
                streamLengths.Add(maxEndOffset);
            }

            return streamLengths;
        }

        private static int GetFormatSize(VertexFormatV2 format)
        {
            return format switch
            {
                VertexFormatV2.Float => 4,
                VertexFormatV2.Float16 => 2,
                VertexFormatV2.UNorm8 => 1,
                VertexFormatV2.SNorm8 => 1,
                VertexFormatV2.UNorm16 => 2,
                VertexFormatV2.SNorm16 => 2,
                VertexFormatV2.UInt8 => 1,
                VertexFormatV2.SInt8 => 1,
                VertexFormatV2.UInt16 => 2,
                VertexFormatV2.SInt16 => 2,
                VertexFormatV2.UInt32 => 4,
                VertexFormatV2.SInt32 => 4,
                _ => throw new Exception($"Unknown format {format}")
            };
        }

        // 根据版本将通道 format 字段统一映射为 VertexFormatV2
        private static VertexFormatV2 ToVertexFormatV2(int format, UnityVersion version)
        {
            if (version.major >= 2019)
            {
                return (VertexFormatV2)format;
            }
            else if (version.major >= 2017)
            {
                return (VertexFormatV1)format switch
                {
                    VertexFormatV1.Float => VertexFormatV2.Float,
                    VertexFormatV1.Float16 => VertexFormatV2.Float16,
                    VertexFormatV1.Color or
                    VertexFormatV1.UNorm8 => VertexFormatV2.UNorm8,
                    VertexFormatV1.SNorm8 => VertexFormatV2.SNorm8,
                    VertexFormatV1.UNorm16 => VertexFormatV2.UNorm16,
                    VertexFormatV1.SNorm16 => VertexFormatV2.SNorm16,
                    VertexFormatV1.UInt8 => VertexFormatV2.UInt8,
                    VertexFormatV1.SInt8 => VertexFormatV2.SInt8,
                    VertexFormatV1.UInt16 => VertexFormatV2.UInt16,
                    VertexFormatV1.SInt16 => VertexFormatV2.SInt16,
                    VertexFormatV1.UInt32 => VertexFormatV2.UInt32,
                    VertexFormatV1.SInt32 => VertexFormatV2.SInt32,
                    _ => throw new Exception($"Unknown format {format}")
                };
            }
            else
            {
                return (VertexChannelFormat)format switch
                {
                    VertexChannelFormat.Float => VertexFormatV2.Float,
                    VertexChannelFormat.Float16 => VertexFormatV2.Float16,
                    VertexChannelFormat.Color => VertexFormatV2.UNorm8,
                    VertexChannelFormat.Byte => VertexFormatV2.UInt8,
                    VertexChannelFormat.UInt32 => VertexFormatV2.UInt32,
                    _ => throw new Exception($"Unknown format {format}")
                };
            }
        }

        private static bool IsFormatInt(VertexFormatV2 format)
        {
            return format switch
            {
                VertexFormatV2.UInt8 => true,
                VertexFormatV2.SInt8 => true,
                VertexFormatV2.UInt16 => true,
                VertexFormatV2.SInt16 => true,
                VertexFormatV2.UInt32 => true,
                VertexFormatV2.SInt32 => true,
                _ => false
            };
        }

        // 获取顶点数据字节流：优先 m_VertexData.m_DataSize，否则按 m_StreamData 从
        // bundle 内（archive:/）或外部 resS 文件读取。
        private static byte[] GetVertexData(AssetsFileInstance fileInst, AssetTypeValueField baseField)
        {
            var usesStreamData = false;
            var offset = 0U;
            var size = 0U;
            var path = string.Empty;

            var streamData = baseField["m_StreamData"];
            if (!streamData.IsDummy)
            {
                offset = streamData["offset"].AsUInt;
                size = streamData["size"].AsUInt;
                path = streamData["path"].AsString;
                usesStreamData = size > 0 && path != string.Empty;
            }

            if (usesStreamData)
            {
                // 1) bundle 内的 archive:/ 路径：直接从 bundle 数据区间读取
                if (fileInst.parentBundle != null && path.StartsWith("archive:/"))
                {
                    var archiveTrimmedPath = path;
                    if (archiveTrimmedPath.StartsWith("archive:/"))
                        archiveTrimmedPath = archiveTrimmedPath.Substring(9);

                    archiveTrimmedPath = Path.GetFileName(archiveTrimmedPath);

                    AssetBundleFile bundle = fileInst.parentBundle.file;

                    AssetsFileReader reader = bundle.DataReader;
                    AssetBundleDirectoryInfo[] dirInf = bundle.BlockAndDirInfo.DirectoryInfos;
                    for (int i = 0; i < dirInf.Length; i++)
                    {
                        AssetBundleDirectoryInfo info = dirInf[i];
                        if (info.Name == archiveTrimmedPath)
                        {
                            byte[] meshData;
                            lock (bundle.DataReader)
                            {
                                reader.Position = info.Offset + offset;
                                meshData = reader.ReadBytes((int)size);
                            }
                            return meshData;
                        }
                    }
                }

                // 2) 外部 resS 文件：在序列化文件同目录查找
                var rootPath = Path.GetDirectoryName(fileInst.path)
                    ?? throw new FileNotFoundException("Can't find resS for mesh");

                var fixedStreamPath = path;

                // 用户可能已将序列化文件与 resS 从 bundle 中提取到磁盘
                var bundleInst = fileInst.parentBundle;
                if (bundleInst == null && path.StartsWith("archive:/"))
                {
                    fixedStreamPath = Path.GetFileName(fixedStreamPath);
                }
                if (!Path.IsPathRooted(fixedStreamPath) && rootPath != null)
                {
                    fixedStreamPath = Path.Combine(rootPath, fixedStreamPath);
                }

                if (File.Exists(fixedStreamPath))
                {
                    var stream = File.OpenRead(fixedStreamPath);
                    stream.Position = offset;
                    var data = new byte[size];
                    stream.Read(data, 0, (int)size);
                    return data;
                }
                // 3) 可能是 data.unity3d 之类的 bundle（无 archive:/ 前缀），按文件名在 bundle 目录中查找
                else if (bundleInst != null && TryGetBundleFileIndex(bundleInst.file, path, out var fileIdx))
                {
                    var bundle = bundleInst.file;
                    bundle.GetFileRange(fileIdx, out var bunOffset, out var _);
                    var reader = bundle.DataReader;
                    reader.Position = bunOffset + offset;
                    return reader.ReadBytes((int)size);
                }
                else
                {
                    throw new FileNotFoundException("Can't find resS for mesh");
                }
            }
            else
            {
                return baseField["m_VertexData"]["m_DataSize"].AsByteArray;
            }
        }

        private static bool TryGetBundleFileIndex(AssetBundleFile bunFile, string name, out int dirInf)
        {
            dirInf = Array.FindIndex(bunFile.BlockAndDirInfo.DirectoryInfos, i => i.Name == name);
            return dirInf != -1;
        }

        // 按 stream / channel 拆分顶点数据，逐通道解码并写入对应数组
        private void ReadVertexData(AssetsFileInstance fileInst, AssetTypeValueField baseField, UnityVersion version)
        {
            var vertexCount = baseField["m_VertexData"]["m_VertexCount"].AsInt;
            var vertexData = GetVertexData(fileInst, baseField);
            var streamLengths = GetStreamLengths(version);
            var startPos = 0;
            for (var strIdx = 0; strIdx < streamLengths.Count; strIdx++)
            {
                var streamLength = streamLengths[strIdx];
                for (var chnIdx = 0; chnIdx < Channels.Count; chnIdx++)
                {
                    var channel = Channels[chnIdx];
                    if (channel.stream != strIdx)
                        continue;

                    var dimension = channel.dimension & 0xf;
                    var vertexFormat = ToVertexFormatV2(channel.format, version);
                    var offset = channel.offset + startPos;
                    var size = GetFormatSize(vertexFormat) * dimension;
                    var data = new byte[size * vertexCount];
                    for (var i = 0; i < vertexCount; i++)
                    {
                        Buffer.BlockCopy(vertexData, offset + i * streamLength, data, i * size, size);
                    }

                    int[]? intItems = null;
                    float[]? floatItems = null;
                    if (IsFormatInt(vertexFormat))
                        intItems = ConvertIntArray(data, dimension, vertexFormat);
                    else
                        floatItems = ConvertFloatArray(data, dimension, vertexFormat);

                    SetCorrectArray(intItems!, floatItems!, chnIdx, version);
                }
                startPos += streamLengths[strIdx] * vertexCount;
            }
        }

        // 按通道索引与 Unity 版本，将解码结果写入 Vertices / Normals / UVs 等字段
        private void SetCorrectArray(int[] intItems, float[] floatItems, int channelIndex, UnityVersion version)
        {
            if (version.major >= 2018)
            {
                var channelType = (ChannelTypeV3)channelIndex;
                switch (channelType)
                {
                    case ChannelTypeV3.Vertex: Vertices = floatItems; break;
                    case ChannelTypeV3.Normal: Normals = floatItems; break;
                    case ChannelTypeV3.Tangent: Tangents = floatItems; break;
                    case ChannelTypeV3.Color: Colors = floatItems; break;
                    case ChannelTypeV3.TexCoord0:
                    case ChannelTypeV3.TexCoord1:
                    case ChannelTypeV3.TexCoord2:
                    case ChannelTypeV3.TexCoord3:
                    case ChannelTypeV3.TexCoord4:
                    case ChannelTypeV3.TexCoord5:
                    case ChannelTypeV3.TexCoord6:
                    case ChannelTypeV3.TexCoord7:
                    {
                        if (UVs.Length == 0)
                        {
                            UVs = new float[8][];
                        }
                        UVs[(int)channelType - (int)ChannelTypeV3.TexCoord0] = floatItems;
                        break;
                    }
                    case ChannelTypeV3.BlendWeight:
                    case ChannelTypeV3.BlendIndices:
                    {
                        // 预览暂不使用蒙皮数据
                        break;
                    }
                }
            }
            else // version.major >= 5
            {
                var channelType = (ChannelTypeV2)channelIndex;
                switch (channelType)
                {
                    case ChannelTypeV2.Vertex: Vertices = floatItems; break;
                    case ChannelTypeV2.Normal: Normals = floatItems; break;
                    case ChannelTypeV2.Color: Colors = floatItems; break;
                    case ChannelTypeV2.TexCoord0:
                    case ChannelTypeV2.TexCoord1:
                    case ChannelTypeV2.TexCoord2:
                    case ChannelTypeV2.TexCoord3:
                    {
                        if (UVs.Length == 0)
                        {
                            UVs = new float[4][];
                        }
                        UVs[(int)channelType - (int)ChannelTypeV2.TexCoord0] = floatItems;
                        break;
                    }
                    case ChannelTypeV2.Tangent: Tangents = floatItems; break;
                }
            }
        }

        private static int[] ConvertIntArray(byte[] data, int dims, VertexFormatV2 format)
        {
            var size = GetFormatSize(format);
            var count = data.Length / size;
            var items = new int[count];
            switch (format)
            {
                case VertexFormatV2.UInt8:
                case VertexFormatV2.SInt8:
                {
                    for (var i = 0; i < count; i++)
                    {
                        items[i] = data[i];
                    }
                    return items;
                }
                case VertexFormatV2.UInt16:
                case VertexFormatV2.SInt16:
                {
                    var src = 0;
                    for (var i = 0; i < count; i++, src += 2)
                    {
                        items[i] = data[src] | data[src + 1] << 8;
                    }
                    return items;
                }
                case VertexFormatV2.UInt32:
                case VertexFormatV2.SInt32:
                {
                    var src = 0;
                    for (var i = 0; i < count; i++, src += 4)
                    {
                        items[i] = data[src] | data[src + 1] << 8 | data[src + 2] << 16 | data[src + 3] << 24;
                    }
                    return items;
                }
                default:
                    throw new Exception($"Unknown format {format}");
            }
        }

        private static float[] ConvertFloatArray(byte[] data, int dims, VertexFormatV2 format)
        {
            var size = GetFormatSize(format);
            var count = data.Length / size;
            var items = new float[count];
            switch (format)
            {
                case VertexFormatV2.Float:
                {
                    var src = 0;
                    for (var i = 0; i < count; i++, src += 4)
                    {
                        items[i] = BitConverter.ToSingle(data, src);
                    }
                    return items;
                }
                case VertexFormatV2.Float16:
                {
                    var src = 0;
                    for (var i = 0; i < count; i++, src += 2)
                    {
                        items[i] = (float)BitConverter.UInt16BitsToHalf((ushort)(data[src] | data[src + 1] << 8));
                    }
                    return items;
                }
                case VertexFormatV2.UNorm8:
                {
                    for (var i = 0; i < count; i++)
                    {
                        items[i] = data[i] / 255f;
                    }
                    return items;
                }
                case VertexFormatV2.SNorm8:
                {
                    for (var i = 0; i < count; i++)
                    {
                        items[i] = Math.Max((sbyte)data[i] / 127f, -1f);
                    }
                    return items;
                }
                case VertexFormatV2.UNorm16:
                {
                    var src = 0;
                    for (var i = 0; i < count; i++, src += 2)
                    {
                        items[i] = (data[src] | data[src + 1] << 8) / 65535f;
                    }
                    return items;
                }
                case VertexFormatV2.SNorm16:
                {
                    var src = 0;
                    for (var i = 0; i < count; i++, src += 2)
                    {
                        items[i] = Math.Max((short)(data[src] | data[src + 1] << 8) / 32767f, -1f);
                    }
                    return items;
                }
                default:
                    throw new Exception($"Unknown format {format}");
            }
        }
    }
}
