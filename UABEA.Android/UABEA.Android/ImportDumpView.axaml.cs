using AssetsTools.NET.Extra;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using UABEAvalonia;

namespace UABEA.Android
{
    public partial class ImportDumpView : UserControl
    {
        public enum DumpFormat { Text, Json }

        private AssetWorkspace? _workspace;
        private string _dir = "";
        private DumpFormat _format;
        private ObservableCollection<ImportDumpRow> _rows = new();

        public event EventHandler<List<ImportDumpInfo>?>? Confirmed;

        public ImportDumpView()
        {
            InitializeComponent();
            matchList.ItemsSource = _rows;
            btnConfirm.Click += BtnConfirm_Click;
            btnCancel.Click += BtnCancel_Click;
        }

        public void Initialize(AssetWorkspace workspace, List<AssetContainer> selection, string dir, DumpFormat format)
        {
            _workspace = workspace;
            _dir = dir;
            _format = format;
            _rows.Clear();

            string ext = format == DumpFormat.Json ? "json" : "txt";
            formatHint.Text = format == DumpFormat.Json
                ? "格式: Json (.json) - 对应 AssetImportExport.DumpJsonAsset"
                : "格式: Text (.txt) - 对应 AssetImportExport.DumpTextAsset";

            var extensions = new List<string> { ext };
            var filesInDir = FileUtils.GetFilesInDirectory(dir, extensions);

            int matchedCount = 0;
            foreach (var cont in selection)
            {
                AssetNameUtils.GetDisplayNameFast(workspace, cont, true, out string assetName, out string _);
                string assetFile = Path.GetFileName(cont.FileInstance.path);
                long pathId = cont.PathId;

                string matchName = $"-{assetFile}-{pathId}.{ext}";
                var matchingFiles = filesInDir
                    .Where(f => f.EndsWith(matchName))
                    .Select(Path.GetFileName)
                    .ToList()!;

                var row = new ImportDumpRow
                {
                    AssetName = assetName,
                    AssetFile = assetFile,
                    PathId = pathId,
                    Container = cont,
                    MatchingFiles = matchingFiles,
                    SelectedIndex = matchingFiles.Count > 0 ? 0 : -1
                };
                if (matchingFiles.Count > 0) matchedCount++;
                _rows.Add(row);
            }

            matchSummary.Text = $"共 {selection.Count} 个资产,{matchedCount} 个有匹配文件";
        }

        private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
        {
            var result = new List<ImportDumpInfo>();
            foreach (var row in _rows)
            {
                if (row.SelectedIndex >= 0 && row.SelectedIndex < row.MatchingFiles.Count)
                {
                    result.Add(new ImportDumpInfo
                    {
                        cont = row.Container,
                        importFile = Path.Combine(_dir, row.MatchingFiles[row.SelectedIndex]!),
                        assetName = row.AssetName,
                        assetFile = row.AssetFile,
                        pathId = row.PathId
                    });
                }
            }
            Confirmed?.Invoke(this, result);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            Confirmed?.Invoke(this, null);
        }
    }

    public class ImportDumpInfo
    {
        public AssetContainer cont = null!;
        public string importFile = "";
        public string assetName = "";
        public string assetFile = "";
        public long pathId;
    }

    public class ImportDumpRow : INotifyPropertyChanged
    {
        public string AssetName { get; set; } = "";
        public string AssetFile { get; set; } = "";
        public long PathId { get; set; }
        public AssetContainer Container { get; set; } = null!;
        public List<string> MatchingFiles { get; set; } = new();
        public bool HasMatch => MatchingFiles.Count > 0;
        public string StatusText => HasMatch ? $"{MatchingFiles.Count} 个匹配" : "无匹配文件";

        private int _selectedIndex;
        public int SelectedIndex
        {
            get => _selectedIndex;
            set { _selectedIndex = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedIndex))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
