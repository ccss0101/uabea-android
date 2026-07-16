using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using Silk.NET.OpenGL;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using UABEAvalonia.Mesh;

namespace UABEAvalonia.Controls.MeshPreviewer
{
    /// <summary>
    /// 基于 OpenGL 的 Mesh 3D 预览控件。参考自 UABEANext4 的 MeshPreviewerControl。
    ///
    /// 渲染流程：
    ///   - 通过 Avalonia 的 <see cref="GlInterface"/> 获取过程地址，再用
    ///     <see cref="Silk.NET.OpenGL.GL.GetApi"/> 构造类型安全的 GL 实例。
    ///   - <see cref="OnOpenGlInit"/> 编译着色器并创建 VBO / IBO / VAO。
    ///   - <see cref="OnOpenGlRender"/> 清屏、设置 MVP 矩阵与方向光，绘制三角形。
    ///   - <see cref="OnOpenGlDeinit"/> 释放显存资源。
    ///
    /// 交互：鼠标左键拖拽旋转相机，滚轮缩放。
    /// </summary>
    public class MeshPreviewerControl : OpenGlControlBase, ICustomHitTest
    {
        private GL? _gl;
        private bool _loaded = false;
        private bool _dirtyModel = false;

        private uint _vertexShader;
        private uint _fragmentShader;
        private uint _shaderProgram;
        private uint _vertexBufferObject;
        private uint _indexBufferObject;
        private uint _vertexArrayObject;

        // 相机球坐标参数：_cameraPos2D 为方位/俯仰角，_cameraZoom 为半径
        private Vector3 _cameraPos = new(15f, 0f, 0f);
        private Vector2 _cameraPos2D = new(0f, 0f);
        private float _cameraZoom = 15f;
        private Vector2 _lastPos = new(-1f, -1f);

        private const float PIH_MINUS_EPSILON = (MathF.PI / 2) - 0.0001f;

        public MeshPreviewerControl()
        {
            PointerPressed += MeshPreviewerControl_PointerPressed;
            PointerReleased += MeshPreviewerControl_PointerReleased;
            PointerMoved += MeshPreviewerControl_PointerMoved;
            PointerWheelChanged += MeshPreviewerControl_PointerWheelChanged;

            RecalculateCamera();
        }

        // 响应 ActiveMesh 依赖属性变化：把 MeshObj 转成顶点缓冲结构并标记重建。
        // 使用 OnPropertyChanged 重写而非 Changed.Subscribe，避免对 System.Reactive 的依赖。
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == ActiveMeshProperty)
            {
                UpdateMeshData(change.GetNewValue<MeshObj?>());
            }
        }

        private MeshObj? _activeMesh;

        /// <summary>当前要渲染的 Mesh 数据。绑定到 PreviewerToolViewModel.ActiveMesh。</summary>
        public static readonly DirectProperty<MeshPreviewerControl, MeshObj?> ActiveMeshProperty =
            AvaloniaProperty.RegisterDirect<MeshPreviewerControl, MeshObj?>(
                nameof(ActiveMesh), o => o.ActiveMesh, (o, v) => o.ActiveMesh = v);

        public MeshObj? ActiveMesh
        {
            get => _activeMesh;
            set => SetAndRaise(ActiveMeshProperty, ref _activeMesh, value);
        }

        // 顶点结构：位置 + 法线，与着色器 attribute 布局对应
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct Vertex
        {
            public Vector3 Position;
            public Vector3 Normal;
        }

        // 默认立方体，无 Mesh 加载时显示
        private Vertex[] _points =
        [
            new Vertex { Position = new Vector3(-1.0f, -1.0f, 1.0f), Normal = new Vector3(1.0f, 1.0f, 1.0f) },
            new Vertex { Position = new Vector3( 1.0f, -1.0f, 1.0f), Normal = new Vector3(1.0f, 1.0f, 1.0f) },
            new Vertex { Position = new Vector3( 1.0f, 1.0f, 1.0f), Normal = new Vector3(1.0f, 1.0f, 1.0f) },
            new Vertex { Position = new Vector3(-1.0f, 1.0f, 1.0f), Normal = new Vector3(1.0f, 1.0f, 1.0f) },
            new Vertex { Position = new Vector3(-1.0f, -1.0f, -1.0f), Normal = new Vector3(1.0f, 1.0f, 1.0f) },
            new Vertex { Position = new Vector3( 1.0f, -1.0f, -1.0f), Normal = new Vector3(1.0f, 1.0f, 1.0f) },
            new Vertex { Position = new Vector3( 1.0f, 1.0f, -1.0f), Normal = new Vector3(1.0f, 1.0f, 1.0f) },
            new Vertex { Position = new Vector3(-1.0f, 1.0f, -1.0f), Normal = new Vector3(1.0f, 1.0f, 1.0f) },
        ];

        private ushort[] _indices =
        [
            // face 1
            0, 1, 2,
            2, 3, 0,

            // face 2
            4, 5, 6,
            6, 7, 4,

            // face 3
            0, 3, 7,
            7, 4, 0,

            // face 4
            1, 2, 6,
            6, 5, 1,

            // face 5
            3, 2, 6,
            6, 7, 3,

            // face 6
            0, 1, 5,
            5, 4, 0
        ];

        // ActiveMesh 变化时，把 MeshObj 的位置/法线/索引转成顶点缓冲结构并标记重建
        private void UpdateMeshData(MeshObj? gl)
        {
            if (gl == null)
                return;

            var vertexCount = gl.Vertices.Length / 3;
            _points = new Vertex[vertexCount];

            _indices = gl.Indices;

            if (vertexCount > 0)
            {
                // 法线数量可能与顶点数不一致（旧格式），按比例跳过
                var skip = gl.Normals.Length / vertexCount;
                for (var i = 0; i < vertexCount; i++)
                {
                    _points[i] = new Vertex
                    {
                        Position = new Vector3(gl.Vertices[i * 3], gl.Vertices[i * 3 + 1], gl.Vertices[i * 3 + 2]),
                        Normal = new Vector3(-gl.Normals[i * skip], gl.Normals[i * skip + 1], gl.Normals[i * skip + 2])
                    };
                }
            }

            _dirtyModel = true;
        }

        private void MeshPreviewerControl_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            var curPos = e.GetPosition(this);
            _lastPos.X = (float)curPos.X;
            _lastPos.Y = (float)curPos.Y;
        }

        private void MeshPreviewerControl_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                _lastPos.X = -1f;
                _lastPos.Y = -1f;
            }
        }

        // 鼠标左键拖拽：更新方位/俯仰角并重新计算相机位置
        private void MeshPreviewerControl_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            if (_lastPos.X == -1f && _lastPos.Y == -1f)
                return;

            var curPos = e.GetPosition(this);
            var curPosX = (float)curPos.X;
            var curPosY = (float)curPos.Y;

            _cameraPos2D.X += (_lastPos.X - curPosX) * 0.006f;
            _cameraPos2D.Y += (curPosY - _lastPos.Y) * 0.006f;
            _cameraPos2D.Y = MathF.Max(MathF.Min(_cameraPos2D.Y, PIH_MINUS_EPSILON), -PIH_MINUS_EPSILON);

            RecalculateCamera();

            _lastPos.X = curPosX;
            _lastPos.Y = curPosY;
        }

        // 滚轮缩放
        private void MeshPreviewerControl_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            _cameraZoom *= 1f - (float)e.Delta.Y / 10f;
            RecalculateCamera();
        }

        // 由球坐标计算相机笛卡尔位置
        private void RecalculateCamera()
        {
            _cameraPos.X = _cameraZoom * MathF.Cos(_cameraPos2D.Y) * MathF.Sin(_cameraPos2D.X);
            _cameraPos.Y = _cameraZoom * MathF.Sin(_cameraPos2D.Y);
            _cameraPos.Z = _cameraZoom * MathF.Cos(_cameraPos2D.Y) * MathF.Cos(_cameraPos2D.X);
        }

        public bool HitTest(Point point)
        {
            return true;
        }

        // /////////////

        // 收集并输出 OpenGL 错误，便于调试
        private void CheckError(int id)
        {
            if (_gl is null || !_loaded)
                return;

            GLEnum err;
            while ((err = _gl.GetError()) != GLEnum.NoError)
            {
                Debug.WriteLine($"OGL Error {err} @ {id}");
            }
        }

        // 编译单个着色器，失败时抛出异常
        protected uint LoadShader(ShaderType shaderType, string content)
        {
            if (_gl is null || !_loaded)
                return uint.MaxValue;

            var shaderHnd = _gl.CreateShader(shaderType);
            _gl.ShaderSource(shaderHnd, content);
            _gl.CompileShader(shaderHnd);
            string infoLog = _gl.GetShaderInfoLog(shaderHnd);
            if (!string.IsNullOrWhiteSpace(infoLog))
            {
                throw new Exception($"Error compiling shader of type {shaderType}, failed with error {infoLog}");
            }

            return shaderHnd;
        }

        protected override unsafe void OnOpenGlInit(GlInterface glInterface)
        {
            if (_loaded)
                return;

            _loaded = true;
            base.OnOpenGlInit(glInterface);

            _gl = GL.GetApi(glInterface.GetProcAddress);
            _gl.Enable(EnableCap.DepthTest);

            Debug.WriteLine($"Renderer: {_gl.GetStringS(GLEnum.Renderer)} Version: {_gl.GetStringS(GLEnum.Version)}");

            _vertexShader = LoadShader(ShaderType.VertexShader, MeshPreviewerShaders.VERTEX_SOURCE);
            _fragmentShader = LoadShader(ShaderType.FragmentShader, MeshPreviewerShaders.FRAGMENT_SORUCE);
            CheckError(0);

            _shaderProgram = _gl.CreateProgram();
            _gl.AttachShader(_shaderProgram, _vertexShader);
            _gl.AttachShader(_shaderProgram, _fragmentShader);
            _gl.BindAttribLocation(_shaderProgram, MeshPreviewerShaders.POSITION_LOC, "aPos");
            _gl.BindAttribLocation(_shaderProgram, MeshPreviewerShaders.NORMAL_LOC, "aNormal");
            _gl.LinkProgram(_shaderProgram);
            CheckError(1);

            BuildMesh(_gl);
        }

        // 创建 / 重建 VBO、IBO、VAO 并配置顶点属性指针
        private unsafe void BuildMesh(GL gl)
        {
            _vertexBufferObject = gl.GenBuffer();
            gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBufferObject);
            var vertexSize = Marshal.SizeOf<Vertex>();
            fixed (void* pdata = _points)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_points.Length * vertexSize), pdata, BufferUsageARB.StaticDraw);
            }
            CheckError(2);

            _indexBufferObject = gl.GenBuffer();
            gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBufferObject);
            fixed (void* pdata = _indices)
            {
                gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(_indices.Length * sizeof(ushort)), pdata, BufferUsageARB.StaticDraw);
            }
            CheckError(3);

            _vertexArrayObject = gl.GenVertexArray();
            gl.BindVertexArray(_vertexArrayObject);
            CheckError(4);

            gl.VertexAttribPointer(MeshPreviewerShaders.POSITION_LOC, 3, GLEnum.Float, false, (uint)vertexSize, (void*)0);
            gl.VertexAttribPointer(MeshPreviewerShaders.NORMAL_LOC, 3, GLEnum.Float, false, (uint)vertexSize, (void*)12);
            CheckError(5);

            gl.EnableVertexAttribArray(MeshPreviewerShaders.POSITION_LOC);
            gl.EnableVertexAttribArray(MeshPreviewerShaders.NORMAL_LOC);
            CheckError(6);
        }

        protected override unsafe void OnOpenGlRender(GlInterface glInterface, int fb)
        {
            var gl = GL.GetApi(glInterface.GetProcAddress);

            // Mesh 变更后重建缓冲
            if (_dirtyModel)
            {
                _dirtyModel = false;
                BuildMesh(gl);
            }

            gl.ClearColor(0.05f, 0.59f, 0.867f, 0);
            gl.Clear((uint)(GLEnum.ColorBufferBit | GLEnum.DepthBufferBit));
            gl.Viewport(0, 0, (uint)Bounds.Width, (uint)Bounds.Height);
            CheckError(7);

            gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBufferObject);
            gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBufferObject);
            gl.BindVertexArray(_vertexArrayObject);
            CheckError(8);

            gl.UseProgram(_shaderProgram);
            CheckError(9);

            var projection = Matrix4x4.CreatePerspectiveFieldOfView(
                (float)(Math.PI / 4), (float)(Bounds.Width / Bounds.Height), 0.01f, 1000);

            var view = Matrix4x4.CreateLookAt(_cameraPos, new Vector3(), new Vector3(0, 1, 0));
            var model = Matrix4x4.Identity;
            var modelLoc = gl.GetUniformLocation(_shaderProgram, "uModel");
            var viewLoc = gl.GetUniformLocation(_shaderProgram, "uView");
            var projectionLoc = gl.GetUniformLocation(_shaderProgram, "uProjection");
            gl.UniformMatrix4(modelLoc, 1, false, &model.M11);
            gl.UniformMatrix4(viewLoc, 1, false, &view.M11);
            gl.UniformMatrix4(projectionLoc, 1, false, &projection.M11);
            CheckError(10);

            var directionalLightDirLoc = gl.GetUniformLocation(_shaderProgram, "uDirectionalLightDir");
            var directionalLightColorLoc = gl.GetUniformLocation(_shaderProgram, "uDirectionalLightColor");
            gl.Uniform3(directionalLightDirLoc, -1.0f, -1.0f, -0.7f);
            gl.Uniform3(directionalLightColorLoc, 1.0f, 1.0f, 1.0f);
            CheckError(11);
            gl.DrawElements(PrimitiveType.Triangles, (uint)_indices.Length, DrawElementsType.UnsignedShort, (void*)0);
            CheckError(12);

            RequestNextFrameRendering();
        }

        protected override void OnOpenGlDeinit(GlInterface glInterface)
        {
            if (_gl is null)
                return;

            var gl = _gl;

            gl.BindBuffer(GLEnum.ArrayBuffer, 0);
            gl.BindBuffer(GLEnum.ElementArrayBuffer, 0);
            gl.BindVertexArray(0);
            gl.UseProgram(0);

            gl.DeleteBuffer(_vertexBufferObject);
            gl.DeleteBuffer(_indexBufferObject);
            gl.DeleteVertexArray(_vertexArrayObject);
            gl.DeleteProgram(_shaderProgram);
            gl.DeleteShader(_fragmentShader);
            gl.DeleteShader(_vertexShader);

            _loaded = false;
            _gl = null;
        }
    }
}
