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
    public partial class DataView : UserControl
    {
        public event EventHandler<bool>? Confirmed;

        public DataView()
        {
            InitializeComponent();
            btnClose.Click += BtnClose_Click;
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            // bool=true 表示已查看完毕关闭
            Confirmed?.Invoke(this, true);
        }

        public void Initialize(AssetWorkspace? workspace, AssetContainer? container,
                               BundleFileInstance? bundleInst, BundleWorkspace? bundleWorkspace)
        {
            tree.ItemsSource = null;
            lblStatus.Text = string.Empty;

            if (container == null)
            {
                if (bundleInst != null)
                    SetError("请先选择一个具体资产再查看类型树（bundle 内含多个资产）。");
                else
                    SetError("未提供资产。");
                return;
            }

            // container.FileInstance 对 bundle 内的资产已指向加载好的子 .assets；
            // 通过 GetAssetInfo 取得对应的 AssetFileInfo。
            AssetsFileInstance? fileInst = container.FileInstance;
            if (fileInst == null)
            {
                SetError("资产容器缺少文件实例。");
                return;
            }

            try
            {
                AssetFileInfo? info = fileInst.file.GetAssetInfo(container.PathId);
                AssetsFile afile = fileInst.file;
                AssetsFileMetadata meta = afile.Metadata;

                // 优先使用文件内嵌类型树（含 ByteSize / Align 信息）。
                // 注：AssetsTools.NET 没有 AssetTypeTreeReader，类型树通过
                // AssetsFileMetadata.FindTypeTreeTypeByID / ByScriptIndex 读取。
                TypeTreeType? ttType = null;
                if (meta.TypeTreeEnabled)
                {
                    if (container.ClassId == 0x72 || container.ClassId < 0)
                        ttType = meta.FindTypeTreeTypeByScriptIndex(container.MonoId);
                    else
                        ttType = meta.FindTypeTreeTypeByID(container.ClassId);
                }

                if (ttType != null && ttType.Nodes != null && ttType.Nodes.Count > 0)
                {
                    BuildTreeFromTypeTree(ttType);
                    lblStatus.Text = $"类型树：{ttType.Nodes.Count} 个节点（来自文件内嵌类型树）";
                    return;
                }

                // 退路：文件未内嵌类型树（独立 .assets 常见），用类数据库解码字段结构。
                AssetsManager? am = workspace?.am ?? bundleWorkspace?.am;
                AssetTypeValueField? baseField = null;
                try
                {
                    if (workspace != null)
                        baseField = workspace.GetBaseField(container);
                    else if (am != null && info != null)
                        baseField = am.GetBaseField(fileInst, info);
                }
                catch
                {
                    baseField = null;
                }

                if (baseField != null)
                {
                    BuildTreeFromValueField(baseField);
                    lblStatus.Text = "文件未内嵌类型树，已通过类数据库解码字段结构（无 Size/Align 信息）。";
                }
                else
                {
                    SetError("无法读取类型树：文件未内嵌类型树，且类数据库缺失或解码失败。");
                }
            }
            catch (Exception ex)
            {
                SetError("读取类型树失败：" + ex.Message);
            }
        }

        // 参考 AssetsFileInfoWindow.FillTypeTreeNodeTree：按 TypeTreeNode.Level 构建层级。
        private void BuildTreeFromTypeTree(TypeTreeType type)
        {
            var roots = new ObservableCollection<DataTreeNode>();
            var levelStack = new List<ObservableCollection<DataTreeNode>>();

            string stringBuffer = type.StringBuffer;
            TypeTreeNode baseNode = type.Nodes[0];
            var root = new DataTreeNode { Display = NodeToString(baseNode, stringBuffer) };
            roots.Add(root);
            levelStack.Add(root.Children);

            for (int i = 1; i < type.Nodes.Count; i++)
            {
                TypeTreeNode field = type.Nodes[i];
                var node = new DataTreeNode { Display = NodeToString(field, stringBuffer) };

                int parentLevel = field.Level - 1;
                if (parentLevel >= 0 && parentLevel < levelStack.Count)
                    levelStack[parentLevel].Add(node);
                else
                    roots.Add(node); // 兜底：异常层级直接挂到根

                if (levelStack.Count > field.Level)
                    levelStack[field.Level] = node.Children;
                else
                    levelStack.Add(node.Children);
            }

            tree.ItemsSource = roots;
        }

        private static string NodeToString(TypeTreeNode node, string stringBuffer)
        {
            string typeName = node.GetTypeString(stringBuffer);
            string fieldName = node.GetNameString(stringBuffer);
            bool aligned = (node.MetaFlags & 0x4000) != 0;
            return $"{typeName} {fieldName}  // size={node.ByteSize}{(aligned ? " align" : "")}";
        }

        // 退路：从解码后的 AssetTypeValueField 构建字段结构树（参考 AssetDataTreeView）。
        private void BuildTreeFromValueField(AssetTypeValueField field)
        {
            var root = new DataTreeNode { Display = ValueFieldToString(field) };
            foreach (AssetTypeValueField child in field.Children)
                root.Children.Add(BuildValueNode(child));
            tree.ItemsSource = new ObservableCollection<DataTreeNode> { root };
        }

        private static DataTreeNode BuildValueNode(AssetTypeValueField field)
        {
            var node = new DataTreeNode { Display = ValueFieldToString(field) };
            foreach (AssetTypeValueField child in field.Children)
                node.Children.Add(BuildValueNode(child));
            return node;
        }

        private static string ValueFieldToString(AssetTypeValueField field)
        {
            string middle = string.Empty;
            string value = string.Empty;
            if (field.Value != null)
            {
                AssetValueType evt = field.Value.ValueType;
                if (1 <= (int)evt && (int)evt <= 12)
                {
                    try
                    {
                        string quote = evt == AssetValueType.String ? "\"" : "";
                        value = $" = {quote}{field.AsString}{quote}";
                    }
                    catch { }
                }
                if (evt == AssetValueType.Array)
                    middle = $"  (size {field.Children.Count})";
            }
            return $"{field.TypeName} {field.FieldName}{middle}{value}";
        }

        private void SetError(string message)
        {
            lblStatus.Text = message;
            tree.ItemsSource = null;
        }
    }

    public class DataTreeNode
    {
        public string Display { get; set; } = string.Empty;
        public ObservableCollection<DataTreeNode> Children { get; } = new();
    }
}
