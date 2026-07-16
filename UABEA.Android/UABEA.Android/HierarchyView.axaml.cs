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
    public partial class HierarchyView : UserControl
    {
        private ObservableCollection<HierarchyNode> _rootNodes = new();
        private ObservableCollection<string> _components = new();

        public event EventHandler<bool>? Confirmed;

        public HierarchyView()
        {
            InitializeComponent();
            hierarchyTree.ItemsSource = _rootNodes;
            componentsList.ItemsSource = _components;
            hierarchyTree.SelectionChanged += HierarchyTree_SelectionChanged;
            btnClose.Click += BtnClose_Click;
        }

        public void Initialize(AssetsFileInstance? fileInst, AssetWorkspace? workspace)
        {
            _rootNodes.Clear();
            _components.Clear();
            errorText.IsVisible = false;

            if (fileInst == null || workspace == null)
            {
                ShowError("未打开文件");
                return;
            }

            try
            {
                BuildHierarchy(fileInst, workspace);
                if (_rootNodes.Count == 0)
                {
                    ShowError("未找到 GameObject 资产");
                }
            }
            catch (Exception ex)
            {
                ShowError("无法读取层级: " + ex.Message);
            }
        }

        private void BuildHierarchy(AssetsFileInstance fileInst, AssetWorkspace workspace)
        {
            // 第一遍：收集当前文件中的所有 Transform / RectTransform，
            // 通过 m_Father 判定父子关系，通过 m_GameObject 关联 GameObject。
            var transformNodes = new Dictionary<long, HierarchyNode>();
            var fatherMap = new Dictionary<long, long>();

            foreach (var kv in workspace.LoadedAssets)
            {
                AssetContainer? cont = kv.Value;
                if (cont == null || cont.FileInstance != fileInst)
                    continue;

                int classId = cont.ClassId;
                bool isTransform = classId == (int)AssetClassID.Transform
                                || classId == (int)AssetClassID.RectTransform;
                if (!isTransform)
                    continue;

                AssetTypeValueField? tfmBf = workspace.GetBaseField(cont);
                if (tfmBf == null)
                    continue;

                long pathId = cont.PathId;
                long fatherPathId = tfmBf["m_Father"]["m_PathID"].AsLong;

                // 通过 m_GameObject PPtr 解析关联的 GameObject 容器
                AssetContainer? goCont = workspace.GetAssetContainer(cont.FileInstance, tfmBf["m_GameObject"], false);

                string name = "[missing gameobject]";
                long nodePathId = pathId;
                List<string> components = new List<string>();

                if (goCont != null)
                {
                    nodePathId = goCont.PathId;
                    AssetTypeValueField? goBf = workspace.GetBaseField(goCont);
                    if (goBf != null)
                    {
                        string goName = goBf["m_Name"].AsString;
                        if (!string.IsNullOrEmpty(goName))
                            name = goName;

                        // 读取 m_Component 数组，每个元素最后一个子字段是 PPtr<Component>
                        AssetTypeValueField compArray = goBf["m_Component"]["Array"];
                        foreach (AssetTypeValueField data in compArray)
                        {
                            if (data.Children.Count == 0)
                                continue;
                            AssetTypeValueField component = data[data.Children.Count - 1];
                            AssetContainer? compCont = workspace.GetAssetContainer(goCont.FileInstance, component, false);
                            if (compCont != null)
                                components.Add(GetTypeName(workspace.am, compCont.ClassId));
                        }
                    }
                }

                HierarchyNode node = new HierarchyNode
                {
                    Name = name,
                    PathId = nodePathId,
                    Components = components
                };
                transformNodes[pathId] = node;
                fatherMap[pathId] = fatherPathId;
            }

            // 第二遍：按 m_Father 建立父子关系，father 为 0 或不在本文件中则视为根
            foreach (var kv in fatherMap)
            {
                long pathId = kv.Key;
                long fatherPathId = kv.Value;
                HierarchyNode node = transformNodes[pathId];
                if (fatherPathId != 0 && transformNodes.TryGetValue(fatherPathId, out HierarchyNode? parent))
                    parent.Children.Add(node);
                else
                    _rootNodes.Add(node);
            }
        }

        private string GetTypeName(AssetsManager am, int classId)
        {
            try
            {
                var cldb = am.ClassDatabase;
                var type = cldb.FindAssetClassByID(classId);
                if (type != null)
                    return cldb.GetString(type.Name);
            }
            catch { }
            return $"0x{classId:X}";
        }

        private void HierarchyTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _components.Clear();
            if (e.AddedItems.Count == 0)
                return;
            if (e.AddedItems[0] is not HierarchyNode node)
                return;
            foreach (string c in node.Components)
                _components.Add(c);
        }

        private void BtnClose_Click(object? sender, RoutedEventArgs e)
        {
            Confirmed?.Invoke(this, true);
        }

        private void ShowError(string msg)
        {
            errorText.Text = msg;
            errorText.IsVisible = true;
        }
    }

    public class HierarchyNode
    {
        public string Name { get; set; } = "";
        public long PathId { get; set; }
        public ObservableCollection<HierarchyNode> Children { get; set; } = new();
        public List<string> Components { get; set; } = new();
    }
}
