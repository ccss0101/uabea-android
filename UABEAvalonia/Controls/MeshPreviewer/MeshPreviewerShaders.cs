namespace UABEAvalonia.Controls.MeshPreviewer
{
    /// <summary>
    /// Mesh 预览器使用的 GLSL 着色器源码。参考自 UABEANext4 的 MeshPreviewerShaders.cs。
    /// 使用 GLSL ES 3.00，适配 Avalonia 提供的 OpenGL ES / OpenGL 上下文。
    /// </summary>
    public static class MeshPreviewerShaders
    {
        /// <summary>
        /// 顶点着色器：MVP 变换 + 把法线变换到世界空间传给片段着色器。
        /// </summary>
        public const string VERTEX_SOURCE = @"#version 300 es
precision mediump float;
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aNormal;
uniform mat4 uModel;
uniform mat4 uProjection;
uniform mat4 uView;
out vec3 FragNormal;
void main()
{
    gl_Position = uProjection * uView * uModel * vec4(aPos, 1.0);
    FragNormal = mat3(transpose(inverse(uModel))) * aNormal;
}";

        /// <summary>
        /// 片段着色器：漫反射光照 + 0.3 环境光，输出最终颜色。
        /// </summary>
        public const string FRAGMENT_SORUCE = @"#version 300 es
precision mediump float;
in vec3 FragNormal;
out vec4 FragColor;
uniform vec3 uDirectionalLightDir;
uniform vec3 uDirectionalLightColor;
void main()
{
    vec3 normal = normalize(FragNormal);
    vec3 lightDirection = normalize(uDirectionalLightDir) * 0.8;

    float diff = max(dot(normal, lightDirection), 0.0);
    vec3 diffuse = diff * uDirectionalLightColor + 0.3;

    FragColor = vec4(diffuse, 1.0);
}";

        public const int POSITION_LOC = 0;
        public const int NORMAL_LOC = 1;
    }
}
