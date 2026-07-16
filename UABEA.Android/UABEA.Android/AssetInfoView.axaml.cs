using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UABEAvalonia;

namespace UABEA.Android
{
    public partial class AssetInfoView : UserControl
    {
        public event EventHandler<bool>? Confirmed;

        public AssetInfoView()
        {
            InitializeComponent();
            btnClose.Click += BtnClose_Click;
        }

        public void Initialize(AssetsFileInstance? fileInst, AssetWorkspace? workspace)
        {
            // 重置所有字段为 N/A
            ResetFields();

            if (fileInst == null)
            {
                lblFileName.Text = "N/A (fileInst 为空)";
                lblTypeTreeHint.Text = "无可用文件实例";
                return;
            }

            try
            {
                FillHeader(fileInst);
            }
            catch
            {
                // Header 读取失败已逐字段降级为 N/A
            }

            try
            {
                FillTypeTree(fileInst, workspace);
            }
            catch
            {
                lblTypeTreeHint.Text = "TypeTree 读取失败";
                lstTypeTree.ItemsSource = null;
            }

            try
            {
                FillDeps(fileInst);
            }
            catch
            {
                lstDeps.ItemsSource = null;
            }
        }

        private void ResetFields()
        {
            lblMetadataSize.Text = "N/A";
            lblFileSize.Text = "N/A";
            lblFormat.Text = "N/A";
            lblDataOffset.Text = "N/A";
            lblEndianness.Text = "N/A";
            lblEngineVersion.Text = "N/A";
            lblTargetPlatform.Text = "N/A";
            lblTypeTreeEnabled.Text = "N/A";
            lblFileName.Text = "N/A";
            lblTypeTreeHint.Text = string.Empty;
            lstTypeTree.ItemsSource = null;
            lstDeps.ItemsSource = null;
        }

        private void FillHeader(AssetsFileInstance fileInst)
        {
            // 文件名
            try
            {
                lblFileName.Text = fileInst.name ?? "N/A";
            }
            catch { lblFileName.Text = "N/A"; }

            AssetsFile? afile = fileInst.file;
            if (afile == null)
                return;

            // Header
            try
            {
                AssetsFileHeader header = afile.Header;
                lblMetadataSize.Text = SafeText(() => header.MetadataSize.ToString());
                lblFileSize.Text = SafeText(() => header.FileSize.ToString());
                lblFormat.Text = SafeText(() => header.Version.ToString());
                lblDataOffset.Text = SafeText(() => header.DataOffset.ToString());
                lblEndianness.Text = SafeText(() => header.Endianness ? "big endian" : "little endian");
            }
            catch { /* 保持 N/A */ }

            // Metadata
            try
            {
                AssetsFileMetadata meta = afile.Metadata;
                if (meta != null)
                {
                    lblEngineVersion.Text = SafeText(() => meta.UnityVersion ?? "N/A");
                    lblTargetPlatform.Text = SafeText(() =>
                        $"{(BuildTarget)meta.TargetPlatform} ({meta.TargetPlatform})");
                    lblTypeTreeEnabled.Text = SafeText(() =>
                        meta.TypeTreeEnabled ? "enabled" : "disabled");
                }
            }
            catch { /* 保持 N/A */ }
        }

        private void FillTypeTree(AssetsFileInstance fileInst, AssetWorkspace? workspace)
        {
            AssetsFile? afile = fileInst.file;
            if (afile == null)
            {
                lblTypeTreeHint.Text = "无可用 AssetsFile";
                return;
            }

            AssetsFileMetadata? meta = afile.Metadata;
            if (meta == null)
            {
                lblTypeTreeHint.Text = "无可用 Metadata";
                return;
            }

            if (!meta.TypeTreeEnabled)
            {
                lblTypeTreeHint.Text = "TypeTree 未启用,以下为 ClassDatabase 推断的类型列表";
            }
            else
            {
                lblTypeTreeHint.Text = "共 N 个类型 (TypeId / ScriptId / Hash / MonoHash)";
            }

            var items = new ObservableCollection<TypeTreeRowItem>();

            // 主类型
            List<TypeTreeType>? types = meta.TypeTreeTypes;
            if (types != null)
            {
                AddTypeTreeRows(items, types, workspace, fileInst, isRefType: false);
            }

            // 引用类型
            try
            {
                List<TypeTreeType>? refTypes = meta.RefTypes;
                if (refTypes != null && refTypes.Count > 0)
                {
                    AddTypeTreeRows(items, refTypes, workspace, fileInst, isRefType: true);
                }
            }
            catch { /* RefTypes 读取失败忽略 */ }

            if (items.Count == 0)
            {
                lblTypeTreeHint.Text = "无类型数据";
            }
            else
            {
                lblTypeTreeHint.Text = $"共 {items.Count} 个类型 (TypeId / ScriptId / Hash / MonoHash)";
            }

            lstTypeTree.ItemsSource = items;
        }

        private void AddTypeTreeRows(ObservableCollection<TypeTreeRowItem> items,
            List<TypeTreeType> types, AssetWorkspace? workspace, AssetsFileInstance fileInst, bool isRefType)
        {
            AssetsManager? am = workspace?.am;

            foreach (TypeTreeType type in types)
            {
                string typeIdStr = SafeText(() => $"{type.TypeId} (0x{type.TypeId:x})");
                string scriptIdStr = "N/A";
                string hashStr = "N/A";
                string monoHashStr = "N/A";
                string typeName = "N/A";

                try
                {
                    // ScriptTypeIndex: 0xffff 表示未使用
                    if (type.ScriptTypeIndex != 0xffff)
                    {
                        scriptIdStr = $"{type.ScriptTypeIndex}";
                        // 尝试从脚本信息推断类名
                        try
                        {
                            if (am != null)
                            {
                                AssetTypeReference? scriptInfo =
                                    AssetHelper.GetAssetsFileScriptInfo(am, fileInst, type.ScriptTypeIndex);
                                if (scriptInfo != null && !string.IsNullOrEmpty(scriptInfo.ClassName))
                                    scriptIdStr = $"{type.ScriptTypeIndex} ({scriptInfo.ClassName})";
                            }
                        }
                        catch { /* 忽略脚本类名解析失败 */ }
                    }
                    else
                    {
                        scriptIdStr = "-";
                    }
                }
                catch { scriptIdStr = "N/A"; }

                try
                {
                    if (!type.TypeHash.IsZero())
                        hashStr = type.TypeHash.ToString();
                    else
                        hashStr = "-";
                }
                catch { hashStr = "N/A"; }

                try
                {
                    if (!type.ScriptIdHash.IsZero())
                        monoHashStr = type.ScriptIdHash.ToString();
                    else
                        monoHashStr = "-";
                }
                catch { monoHashStr = "N/A"; }

                // 类型名:优先用 TypeTree 的 Nodes,否则查 ClassDatabase
                try
                {
                    if (type.Nodes != null && type.Nodes.Count > 0)
                    {
                        TypeTreeNode baseField = type.Nodes[0];
                        typeName = baseField.GetTypeString(type.StringBuffer) ?? "N/A";
                    }
                    else if (am != null && am.ClassDatabase != null)
                    {
                        ClassDatabaseFile cldb = am.ClassDatabase;
                        ClassDatabaseType? dbType = cldb.FindAssetClassByID(type.TypeId);
                        if (dbType != null)
                            typeName = cldb.GetString(dbType.Name);
                    }
                }
                catch { /* typeName 保持 N/A */ }

                string refTag = isRefType ? " REF" : string.Empty;
                string display = $"{typeName}{refTag}\nTypeId={typeIdStr}  ScriptId={scriptIdStr}\nHash={hashStr}  MonoHash={monoHashStr}";

                items.Add(new TypeTreeRowItem { Display = display });
            }
        }

        private void FillDeps(AssetsFileInstance fileInst)
        {
            AssetsFile? afile = fileInst.file;
            if (afile == null)
            {
                lstDeps.ItemsSource = null;
                return;
            }

            AssetsFileMetadata? meta = afile.Metadata;
            if (meta == null)
            {
                lstDeps.ItemsSource = null;
                return;
            }

            var items = new ObservableCollection<DependencyRowItem>();

            // 首项为当前文件本身
            items.Add(new DependencyRowItem
            {
                FilePath = SafeText(() => fileInst.name ?? string.Empty),
                AssetPath = "(当前文件)"
            });

            try
            {
                List<AssetsFileExternal>? externals = meta.Externals;
                if (externals != null)
                {
                    for (int i = 0; i < externals.Count; i++)
                    {
                        AssetsFileExternal ext = externals[i];
                        string filePath = SafeText(() => ext.PathName ?? string.Empty);
                        string assetPath = SafeText(() => ext.VirtualAssetPathName ?? string.Empty);
                        if (string.IsNullOrEmpty(filePath))
                            filePath = SafeText(() => ext.OriginalPathName ?? string.Empty);
                        if (string.IsNullOrEmpty(filePath))
                            filePath = SafeText(() => $"Guid={ext.Guid}");

                        items.Add(new DependencyRowItem
                        {
                            FilePath = string.IsNullOrEmpty(filePath) ? "(空)" : filePath,
                            AssetPath = string.IsNullOrEmpty(assetPath) ? "-" : assetPath
                        });
                    }
                }
            }
            catch { /* 已加载的项保留 */ }

            lstDeps.ItemsSource = items;
        }

        private static string SafeText(Func<string> getter)
        {
            try
            {
                return getter() ?? "N/A";
            }
            catch
            {
                return "N/A";
            }
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            Confirmed?.Invoke(this, true);
        }

        // BuildTarget 枚举(与桌面版 AssetsFileInfoWindow.Header.axaml.cs 保持一致)
        public enum BuildTarget
        {
            StandaloneOSX = 2,
            StandaloneOSXUniversal,
            StandaloneOSXIntel,
            StandaloneWindows,
            WebPlayer,
            WebPlayerStreamed,
            Wii,
            iOS,
            PS3,
            XBOX360,
            StandaloneBroadcom,
            Android,
            StandaloneGLESEmu,
            StandaloneGLES20Emu,
            NaCl,
            StandaloneLinux,
            Flash,
            StandaloneWindows64,
            WebGL,
            WSAPlayer,
            WSAPlayerX64,
            WSAPlayerARM,
            StandaloneLinux64,
            StandaloneLinuxUniversal,
            WP8Player,
            StandaloneOSXIntel64,
            BlackBerry,
            Tizen,
            PSP2,
            PS4,
            PSM,
            XboxOne,
            SamsungTV,
            N3DS,
            WiiU,
            tvOS,
            Switch,
            Lumin,
            Stadia,
            CloudRendering,
            GameCoreXboxSeries,
            GameCoreXboxOne,
            PS5,
            EmbeddedLinux,
            QNX,
            Bratwurst
        }

        private class TypeTreeRowItem
        {
            public string Display { get; set; } = string.Empty;
        }

        private class DependencyRowItem
        {
            public string FilePath { get; set; } = string.Empty;
            public string AssetPath { get; set; } = string.Empty;
        }
    }
}
