using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using UABEAvalonia;
using UABEAvalonia.Mesh;
using AvaloniaVector = Avalonia.Vector;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace UABEA.Android
{
    /// <summary>
    /// 软件渲染 3D Mesh 预览。Android 不支持桌面版 OpenGL + Silk.NET 的 MeshPreviewerControl,
    /// 因此用 Matrix4x4 MVP 变换 + WriteableBitmap 软件光栅化实现。
    /// 支持:拖拽旋转(azimuth/polar)、按钮缩放、wireframe/filled 切换。
    /// </summary>
    public partial class MeshPreviewView : UserControl
    {
        private MeshObj? _mesh;
        private WriteableBitmap? _bitmap;
        private byte[]? _buffer;
        private int _bmpWidth = 800;
        private int _bmpHeight = 480;

        // 相机球坐标
        private float _azimuth = 0.6f;
        private float _polar = 0.5f;
        private float _zoom = 1.0f;
        private bool _wireframe = true;

        // 拖拽状态
        private bool _dragging;
        private NumericsVector2 _lastPointer = new(-1, -1);

        // 渲染降采样阈值
        private const int MaxTrianglesForFullRender = 60000;

        public event EventHandler<bool>? Confirmed;

        public MeshPreviewView()
        {
            InitializeComponent();
            btnZoomIn.Click += (s, e) => { _zoom *= 1.2f; Render(); };
            btnZoomOut.Click += (s, e) => { _zoom /= 1.2f; Render(); };
            btnWireframe.Click += (s, e) => { _wireframe = !_wireframe; Render(); };
            btnResetView.Click += (s, e) => { _azimuth = 0.6f; _polar = 0.5f; _zoom = 1.0f; Render(); };
            btnClose.Click += (s, e) => Confirmed?.Invoke(this, true);

            // 拖拽旋转(触摸 + 鼠标统一走 Pointer 事件)
            previewImage.PointerPressed += (s, e) =>
            {
                _dragging = true;
                var p = e.GetPosition(previewImage);
                _lastPointer = new NumericsVector2((float)p.X, (float)p.Y);
                e.Pointer.Capture(previewImage);
            };
            previewImage.PointerReleased += (s, e) =>
            {
                _dragging = false;
                _lastPointer = new NumericsVector2(-1, -1);
                e.Pointer.Capture(null);
            };
            previewImage.PointerMoved += (s, e) =>
            {
                if (!_dragging) return;
                var p = e.GetPosition(previewImage);
                float dx = (float)p.X - _lastPointer.X;
                float dy = (float)p.Y - _lastPointer.Y;
                _azimuth += dx * 0.01f;
                _polar = Math.Clamp(_polar + dy * 0.01f, -1.5f, 1.5f);
                _lastPointer = new NumericsVector2((float)p.X, (float)p.Y);
                Render();
            };
        }

        public void Initialize(AssetWorkspace? workspace, AssetContainer? container,
            BundleFileInstance? bundleInst, BundleWorkspace? bundleWorkspace)
        {
            try
            {
                if (container == null)
                {
                    ShowError("未选中资产");
                    return;
                }

                AssetsManager? am = workspace?.am ?? bundleWorkspace?.am;
                AssetsFileInstance? fileInst = container.FileInstance;
                if (am == null || fileInst == null)
                {
                    ShowError("缺少 AssetsManager 或文件实例");
                    return;
                }

                AssetFileInfo info = fileInst.file.GetAssetInfo(container.PathId);
                AssetTypeValueField baseField;
                try
                {
                    // 优先走 workspace(已加载)
                    if (workspace != null && container.HasValueField)
                        baseField = container.BaseValueField;
                    else if (workspace != null)
                        baseField = workspace.GetBaseField(container);
                    else
                        baseField = am.GetBaseField(fileInst, info);
                }
                catch (Exception ex)
                {
                    ShowError("读取 BaseField 失败: " + ex.Message);
                    return;
                }

                if (baseField == null)
                {
                    ShowError("无法读取资产字段");
                    return;
                }

                _mesh = MeshObj.FromBaseField(baseField, fileInst);
                if (_mesh.Vertices == null || _mesh.Vertices.Length < 3)
                {
                    ShowError("Mesh 无顶点数据");
                    return;
                }

                int triCount = _mesh.Indices?.Length / 3 ?? 0;
                int vertCount = _mesh.Vertices.Length / 3;
                meshInfo.Text = $"顶点 {vertCount} · 三角形 {triCount}";
                hintText.IsVisible = true;

                AllocateBitmap();
                Render();
            }
            catch (Exception ex)
            {
                ShowError("Mesh 加载失败: " + ex.Message);
            }
        }

        private void ShowError(string msg)
        {
            errorText.Text = msg;
            errorText.IsVisible = true;
            hintText.IsVisible = false;
            meshInfo.Text = "";
        }

        private void AllocateBitmap()
        {
            _bitmap = new WriteableBitmap(
                new PixelSize(_bmpWidth, _bmpHeight),
                new AvaloniaVector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
            _buffer = new byte[_bmpWidth * _bmpHeight * 4];
            previewImage.Source = _bitmap;
        }

        /// <summary>清空缓冲为黑色背景</summary>
        private void ClearBuffer()
        {
            if (_buffer == null) return;
            // BGRA: B=10 G=10 R=10 A=FF 深灰背景
            for (int i = 0; i < _buffer.Length; i += 4)
            {
                _buffer[i] = 18;     // B
                _buffer[i + 1] = 18; // G
                _buffer[i + 2] = 18; // R
                _buffer[i + 3] = 255;// A
            }
        }

        private void SetPixel(int x, int y, byte b, byte g, byte r)
        {
            if (_buffer == null) return;
            if (x < 0 || x >= _bmpWidth || y < 0 || y >= _bmpHeight) return;
            int idx = (y * _bmpWidth + x) * 4;
            _buffer[idx] = b;
            _buffer[idx + 1] = g;
            _buffer[idx + 2] = r;
            _buffer[idx + 3] = 255;
        }

        /// <summary>Bresenham 画线</summary>
        private void DrawLine(int x0, int y0, int x1, int y1, byte b, byte g, byte r)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, e2;
            while (true)
            {
                SetPixel(x0, y0, b, g, r);
                if (x0 == x1 && y0 == y1) break;
                e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>简单三角形填充(扫描线),按顶点亮度着色</summary>
        private void FillTriangle(float ax, float ay, float bx, float by, float cx, float cy, byte b, byte g, byte r)
        {
            // 按 y 排序 a.y <= b.y <= c.y
            if (by < ay) { (ay, by) = (by, ay); (ax, bx) = (bx, ax); }
            if (cy < by) { (by, cy) = (cy, by); (bx, cx) = (cx, bx); }
            if (by < ay) { (ay, by) = (by, ay); (ax, bx) = (bx, ax); }

            int yStart = Math.Max(0, (int)ay);
            int yEnd = Math.Min(_bmpHeight - 1, (int)cy);
            if (yEnd <= yStart) return;

            for (int y = yStart; y <= yEnd; y++)
            {
                // 计算左右 x 边界(用线性插值)
                float t1 = (cy == ay) ? 0 : (y - ay) / (cy - ay);
                float xAC = ax + (cx - ax) * t1;

                float xOther;
                if (y < by)
                {
                    float t2 = (by == ay) ? 0 : (y - ay) / (by - ay);
                    xOther = ax + (bx - ax) * t2;
                }
                else
                {
                    float t2 = (cy == by) ? 0 : (y - by) / (cy - by);
                    xOther = bx + (cx - bx) * t2;
                }

                int xL = (int)Math.Min(xAC, xOther);
                int xR = (int)Math.Max(xAC, xOther);
                xL = Math.Max(0, xL);
                xR = Math.Min(_bmpWidth - 1, xR);
                for (int x = xL; x <= xR; x++)
                    SetPixel(x, y, b, g, r);
            }
        }

        public void Render()
        {
            if (_mesh == null || _buffer == null || _bitmap == null) return;

            ClearBuffer();

            // 构造 MVP
            float eyeDist = 5.0f / _zoom;
            float ex = eyeDist * MathF.Cos(_polar) * MathF.Sin(_azimuth);
            float ey = eyeDist * MathF.Sin(_polar);
            float ez = eyeDist * MathF.Cos(_polar) * MathF.Cos(_azimuth);

            var view = Matrix4x4.CreateLookAt(new NumericsVector3(ex, ey, ez), NumericsVector3.Zero, NumericsVector3.UnitY);
            float aspect = (float)_bmpWidth / _bmpHeight;
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3.0f, aspect, 0.1f, 1000f);
            var vp = view * proj;

            // 光照方向(归一化)
            var lightDir = NumericsVector3.Normalize(new NumericsVector3(0.3f, 0.8f, 0.5f));

            // 计算包围盒以归一化模型坐标到 [-1,1]
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            var verts = _mesh.Vertices;
            for (int i = 0; i < verts.Length; i += 3)
            {
                float x = verts[i], y = verts[i + 1], z = verts[i + 2];
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
            }
            float cx0 = (minX + maxX) * 0.5f;
            float cy0 = (minY + maxY) * 0.5f;
            float cz0 = (minZ + maxZ) * 0.5f;
            float size = Math.Max(Math.Max(maxX - minX, maxY - minY), maxZ - minZ);
            float scale = size > 0.0001f ? 2.0f / size : 1.0f;

            // 变换后的屏幕坐标缓存
            var indices = _mesh.Indices;
            int triCount = indices.Length / 3;

            // 降采样:三角形过多时跳着画
            int stride = triCount > MaxTrianglesForFullRender ? triCount / MaxTrianglesForFullRender : 1;

            for (int t = 0; t < triCount; t += stride)
            {
                int i0 = indices[t * 3];
                int i1 = indices[t * 3 + 1];
                int i2 = indices[t * 3 + 2];

                if (i0 * 3 + 2 >= verts.Length || i1 * 3 + 2 >= verts.Length || i2 * 3 + 2 >= verts.Length)
                    continue;

                // 模型坐标归一化到 [-1,1]
                var v0 = new NumericsVector4((verts[i0 * 3] - cx0) * scale, (verts[i0 * 3 + 1] - cy0) * scale, (verts[i0 * 3 + 2] - cz0) * scale, 1);
                var v1 = new NumericsVector4((verts[i1 * 3] - cx0) * scale, (verts[i1 * 3 + 1] - cy0) * scale, (verts[i1 * 3 + 2] - cz0) * scale, 1);
                var v2 = new NumericsVector4((verts[i2 * 3] - cx0) * scale, (verts[i2 * 3 + 1] - cy0) * scale, (verts[i2 * 3 + 2] - cz0) * scale, 1);

                // 应用 view*proj
                v0 = NumericsVector4.Transform(v0, vp);
                v1 = NumericsVector4.Transform(v1, vp);
                v2 = NumericsVector4.Transform(v2, vp);

                if (v0.W <= 0 || v1.W <= 0 || v2.W <= 0) continue;

                // 透视除法 + NDC → 屏幕坐标
                float sx0 = (v0.X / v0.W * 0.5f + 0.5f) * _bmpWidth;
                float sy0 = (1 - (v0.Y / v0.W * 0.5f + 0.5f)) * _bmpHeight;
                float sx1 = (v1.X / v1.W * 0.5f + 0.5f) * _bmpWidth;
                float sy1 = (1 - (v1.Y / v1.W * 0.5f + 0.5f)) * _bmpHeight;
                float sx2 = (v2.X / v2.W * 0.5f + 0.5f) * _bmpWidth;
                float sy2 = (1 - (v2.Y / v2.W * 0.5f + 0.5f)) * _bmpHeight;

                // 颜色:用法线 · 光照方向计算亮度(无光照信息则全灰)
                byte b = 160, g = 160, r = 160;
                if (_mesh.Normals != null && _mesh.Normals.Length >= verts.Length)
                {
                    var n0 = new NumericsVector3(_mesh.Normals[i0 * 3], _mesh.Normals[i0 * 3 + 1], _mesh.Normals[i0 * 3 + 2]);
                    var n1 = new NumericsVector3(_mesh.Normals[i1 * 3], _mesh.Normals[i1 * 3 + 1], _mesh.Normals[i1 * 3 + 2]);
                    var n2 = new NumericsVector3(_mesh.Normals[i2 * 3], _mesh.Normals[i2 * 3 + 1], _mesh.Normals[i2 * 3 + 2]);
                    var n = NumericsVector3.Normalize((n0 + n1 + n2) * 0.3333f);
                    float intensity = Math.Max(0.15f, NumericsVector3.Dot(n, lightDir));
                    byte cb = (byte)(40 * intensity);
                    byte cg = (byte)(160 * intensity);
                    byte cr = (byte)(210 * intensity);
                    b = cb; g = cg; r = cr;
                }

                if (_wireframe)
                {
                    DrawLine((int)sx0, (int)sy0, (int)sx1, (int)sy1, 180, 220, 255);
                    DrawLine((int)sx1, (int)sy1, (int)sx2, (int)sy2, 180, 220, 255);
                    DrawLine((int)sx2, (int)sy2, (int)sx0, (int)sy0, 180, 220, 255);
                }
                else
                {
                    FillTriangle(sx0, sy0, sx1, sy1, sx2, sy2, b, g, r);
                }
            }

            FlushToBitmap();
        }

        private void FlushToBitmap()
        {
            if (_bitmap == null || _buffer == null) return;
            using (var fb = _bitmap.Lock())
            {
                Marshal.Copy(_buffer, 0, fb.Address, _buffer.Length);
            }
        }
    }
}
