using AssetsTools.NET.Extra;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using UABEAvalonia;

namespace UABEA.Android
{
    public partial class FilterTypeView : UserControl
    {
        private ObservableCollection<FilterTypeRow> _rows = new();

        public event EventHandler<HashSet<int>?>? Confirmed;

        public FilterTypeView()
        {
            InitializeComponent();
            typeList.ItemsSource = _rows;
            btnSelectAll.Click += (s, e) => SetAll(true);
            btnDeselectAll.Click += (s, e) => SetAll(false);
            btnConfirm.Click += BtnConfirm_Click;
            btnCancel.Click += BtnCancel_Click;
        }

        private void SetAll(bool enabled)
        {
            foreach (var r in _rows) r.Enabled = enabled;
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            int enabledCount = _rows.Count(r => r.Enabled);
            summary.Text = $"显示 {enabledCount}/{_rows.Count} 个类型";
        }

        public void Initialize(AssetWorkspace workspace, HashSet<int> checkedClassIds, AssetsManager am)
        {
            _rows.Clear();

            var usedClassIds = workspace.LoadedAssets.Values
                .Select(c => c.ClassId)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            foreach (int classId in usedClassIds)
            {
                string typeName = GetTypeName(am, classId);
                var row = new FilterTypeRow
                {
                    ClassId = classId,
                    TypeName = typeName,
                    Enabled = checkedClassIds.Contains(classId)
                };
                row.PropertyChanged += (s, e) => UpdateSummary();
                _rows.Add(row);
            }
            UpdateSummary();
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

        private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
        {
            var filteredOut = new HashSet<int>();
            foreach (var r in _rows)
            {
                if (!r.Enabled) filteredOut.Add(r.ClassId);
            }
            Confirmed?.Invoke(this, filteredOut);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            Confirmed?.Invoke(this, null);
        }
    }

    public class FilterTypeRow : INotifyPropertyChanged
    {
        public int ClassId { get; set; }
        public string TypeName { get; set; } = "";

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
