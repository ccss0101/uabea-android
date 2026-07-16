using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using UABEAvalonia;
using UABEAvalonia.Mesh;
using UABEAvalonia.Plugins;

namespace MeshPlugin
{
    /// <summary>
    /// Mesh 预览器插件。实现 <see cref="IUavPluginPreviewer"/>，向统一 Previewer 面板提供 3D 网格预览。
    /// 参考自 UABEANext4 的 MeshPlugin.MeshPreviewer，适配本项目的
    /// <see cref="AssetContainer"/> / <see cref="Workspace"/> 类型。
    ///
    /// 支持两种入口：
    ///   - 直接选中 Mesh 资产（ClassId 43）
    ///   - 选中 GameObject（ClassId 1），经 GameObject -> MeshFilter -> Mesh 解析
    /// </summary>
    public class MeshPreviewer : IUavPluginPreviewer
    {
        public string Name => "Mesh Previewer";
        public string Description => "Preview Meshes";

        // 判断 GameObject 是否挂有 MeshFilter 组件（ClassId 33）
        private static bool IsGameObjectWithMeshFilter(Workspace workspace, AssetContainer goAsset)
        {
            if (goAsset.ClassId != (int)AssetClassID.GameObject)
                return false;

            var goBase = workspace.GetBaseField(goAsset);
            if (goBase is null)
                return false;

            var goComponents = goBase["m_Component.Array"];
            foreach (var componentPair in goComponents)
            {
                // 组件对最后一个子字段是 PPtr（不同版本字段名不同，取最后一个最稳妥）
                var component = componentPair[componentPair.Children.Count - 1];
                var componentCont = workspace.GetAssetContainer(goAsset.FileInstance, component);
                if (componentCont is not null && componentCont.ClassId == (int)AssetClassID.MeshFilter)
                {
                    return true;
                }
            }

            return false;
        }

        // 从 GameObject 的 MeshFilter 组件解析出 Mesh 资产容器
        private static AssetContainer? GetMeshFromGameObject(Workspace workspace, AssetContainer goAsset)
        {
            var goBase = workspace.GetBaseField(goAsset);
            if (goBase is null)
                return null;

            var goComponents = goBase["m_Component.Array"];
            foreach (var componentPair in goComponents)
            {
                var component = componentPair[componentPair.Children.Count - 1];
                var componentCont = workspace.GetAssetContainer(goAsset.FileInstance, component);
                if (componentCont is not null && componentCont.ClassId == (int)AssetClassID.MeshFilter)
                {
                    var mfiltBase = workspace.GetBaseField(componentCont);
                    if (mfiltBase is null)
                        return null;

                    // MeshFilter.m_Mesh 指向真正的 Mesh 资产
                    var meshCont = workspace.GetAssetContainer(componentCont.FileInstance, mfiltBase["m_Mesh"]);
                    if (meshCont is null)
                        return null;

                    return meshCont;
                }
            }

            return null;
        }

        public UavPluginPreviewerType SupportsPreview(Workspace workspace, List<AssetContainer> selection)
        {
            if (selection == null || selection.Count == 0)
                return UavPluginPreviewerType.None;

            var asset = selection[0];
            return asset.ClassId == (int)AssetClassID.Mesh || IsGameObjectWithMeshFilter(workspace, asset)
                ? UavPluginPreviewerType.Mesh
                : UavPluginPreviewerType.None;
        }

        public MeshObj? ExecuteMesh(Workspace workspace, List<AssetContainer> selection)
        {
            if (selection == null || selection.Count == 0)
                return null;

            try
            {
                var asset = selection[0];

                // 选中 GameObject 时，先走 GameObject -> MeshFilter -> Mesh 链
                if (asset.ClassId == (int)AssetClassID.GameObject)
                {
                    var maybeMeshAsset = GetMeshFromGameObject(workspace, asset);
                    if (maybeMeshAsset is null)
                        return null;

                    asset = maybeMeshAsset;
                }

                var meshBf = workspace.GetBaseField(asset);
                if (meshBf == null)
                    return null;

                return MeshObj.FromBaseField(meshBf, asset.FileInstance);
            }
            catch
            {
                return null;
            }
        }

        // 本预览器只支持 Mesh，文本 / 图像入口不予处理
        public string ExecuteText(Workspace workspace, List<AssetContainer> selection)
            => throw new NotSupportedException("MeshPreviewer only supports Mesh preview.");

        public byte[] ExecuteImage(Workspace workspace, List<AssetContainer> selection, out int width, out int height)
            => throw new NotSupportedException("MeshPreviewer only supports Mesh preview.");

        public void Cleanup() { }
    }
}
