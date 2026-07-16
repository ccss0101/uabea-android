using AssetsTools.NET.Extra;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UABEAvalonia;

namespace UABEA.Android
{
    public partial class AddAssetView : UserControl
    {
        public class AddAssetInfo
        {
            public long PathId;       // -1 表示自动分配
            public int ClassId;       // TypeId
            public uint MonoScriptId; // 0 表示非 MonoBehaviour
            public bool UseTypeTree;  // true=按 typetree 创建,false=按 classdatabase 创建空资产
        }

        public event EventHandler<AddAssetInfo?>? Confirmed;

        public AddAssetView()
        {
            InitializeComponent();
            btnConfirm.Click += BtnConfirm_Click;
            btnCancel.Click += BtnCancel_Click;
            cbCommonTypes.SelectionChanged += CbCommonTypes_SelectionChanged;
            PopulateCommonTypes();
        }

        private void PopulateCommonTypes()
        {
            var presets = new (string Name, int Id)[]
            {
                ("GameObject (1)", 1),
                ("Transform (4)", 4),
                ("Material (21)", 21),
                ("Mesh (43)", 43),
                ("Shader (48)", 48),
                ("TextAsset (49)", 49),
                ("Texture2D (28)", 28),
                ("AudioClip (83)", 83),
                ("Font (128)", 128),
                ("MonoBehaviour (114)", 114),
                ("Sprite (212)", 212),
            };

            foreach (var (name, id) in presets)
            {
                cbCommonTypes.Items.Add(new ComboBoxItem { Content = name, Tag = id });
            }
        }

        public void Initialize(AssetWorkspace? workspace)
        {
            if (workspace == null)
                return;

            try
            {
                var am = workspace.am;
                var existingIds = new HashSet<int>();
                foreach (var item in cbCommonTypes.Items)
                {
                    if (item is ComboBoxItem ci && ci.Tag is int tagId)
                        existingIds.Add(tagId);
                }

                var usedClassIds = workspace.LoadedAssets.Values
                    .Select(c => c.ClassId)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();

                foreach (int classId in usedClassIds)
                {
                    if (existingIds.Contains(classId))
                        continue;
                    string typeName = GetTypeName(am, classId);
                    cbCommonTypes.Items.Add(new ComboBoxItem { Content = $"{typeName} ({classId})", Tag = classId });
                    existingIds.Add(classId);
                }
            }
            catch
            {
                // 忽略,常用类型仍可用
            }
        }

        private static string GetTypeName(AssetsManager am, int classId)
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

        public void Reset()
        {
            boxPathId.Text = "";
            boxClassId.Text = "";
            boxMonoId.Text = "0";
            chkUseTypeTree.IsChecked = false;
            cbCommonTypes.SelectedIndex = -1;
            errorText.IsVisible = false;
            boxClassId.Focus();
        }

        private void CbCommonTypes_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (cbCommonTypes.SelectedItem is ComboBoxItem item && item.Tag is int id)
            {
                boxClassId.Text = id.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
        {
            // PathId: 留空 = -1 (自动分配)
            string pathIdText = (boxPathId.Text ?? "").Trim();
            long pathId = -1;
            if (pathIdText.Length > 0)
            {
                if (!long.TryParse(pathIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out pathId))
                {
                    ShowError("PathID 不是有效数字");
                    return;
                }
            }

            // ClassId: 必填
            string classIdText = (boxClassId.Text ?? "").Trim();
            if (!int.TryParse(classIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int classId))
            {
                ShowError("ClassID 无效,请输入整数(如 28=Texture2D)");
                return;
            }

            // MonoScriptId: 默认 0
            string monoIdText = (boxMonoId.Text ?? "").Trim();
            uint monoScriptId = 0;
            if (monoIdText.Length > 0)
            {
                if (!uint.TryParse(monoIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out monoScriptId))
                {
                    ShowError("MonoScriptID 无效,请输入非负整数");
                    return;
                }
            }

            bool useTypeTree = chkUseTypeTree.IsChecked ?? false;

            errorText.IsVisible = false;
            Confirmed?.Invoke(this, new AddAssetInfo
            {
                PathId = pathId,
                ClassId = classId,
                MonoScriptId = monoScriptId,
                UseTypeTree = useTypeTree
            });
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            Confirmed?.Invoke(this, null);
        }

        private void ShowError(string msg)
        {
            errorText.Text = msg;
            errorText.IsVisible = true;
        }
    }
}
