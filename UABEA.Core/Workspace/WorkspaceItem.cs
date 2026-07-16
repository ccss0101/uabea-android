using AssetsTools.NET.Extra;
using System.Collections.Generic;
using System.IO;

namespace UABEAvalonia
{
    // 工作区中的树形文件项。
    // 根级文件（独立的 bundle / assets / resource）位于 Workspace.RootItems 中；
    // bundle 内部的子文件作为该 bundle 项的 Children，并设置 Parent 指向父 bundle。
    // 同时保存原始名称 OriginalName 以便跟踪重命名操作。
    public class WorkspaceItem
    {
        // 当前显示名称（可能被重命名）
        public string Name { get; set; }

        // 加载时的原始名称，用于跟踪重命名
        public string OriginalName { get; set; }

        // 文件项类型
        public WorkspaceItemType ItemType { get; }

        // 文件数据流。主要用于 ResourceFile / OtherFile；
        // 对于 AssetsFile，数据流由 AssetsFileInstance 自己持有。
        public Stream? Stream { get; set; }

        // 子文件列表。仅 bundle 项会有子文件；其它项为空列表。
        public List<WorkspaceItem> Children { get; }

        // 标记是否被修改（用于后续保存逻辑，P1 阶段实现）
        public bool IsModified { get; set; }

        // 标记是否为新增文件
        public bool IsNew { get; set; }

        // 标记是否被移除
        public bool IsRemoved { get; set; }

        // 父项。bundle 内子文件指向所属的 bundle 项；根级文件为 null。
        public WorkspaceItem? Parent { get; set; }

        // 如果是序列化资产文件，则持有其 AssetsFileInstance
        public AssetsFileInstance? AssetsFileInstance { get; set; }

        // 如果是 bundle 文件，则持有其 BundleFileInstance
        public BundleFileInstance? BundleFileInstance { get; set; }

        // 根级序列化资产文件
        public WorkspaceItem(AssetsFileInstance fileInst)
        {
            Name = fileInst.name;
            OriginalName = Name;
            ItemType = WorkspaceItemType.AssetsFile;
            AssetsFileInstance = fileInst;
            Children = new List<WorkspaceItem>(0);
        }

        // 根级 bundle 文件（Children 初始为空，由 Workspace 填充）
        public WorkspaceItem(BundleFileInstance bunInst)
        {
            Name = bunInst.name;
            OriginalName = Name;
            ItemType = WorkspaceItemType.BundleFile;
            BundleFileInstance = bunInst;
            Children = new List<WorkspaceItem>();
        }

        // bundle 内的子序列化资产文件
        public WorkspaceItem(string name, AssetsFileInstance fileInst)
        {
            Name = name;
            OriginalName = Name;
            ItemType = WorkspaceItemType.AssetsFile;
            AssetsFileInstance = fileInst;
            Children = new List<WorkspaceItem>(0);
        }

        // resource / 其它文件（可作为根级文件或 bundle 内子文件）
        public WorkspaceItem(string name, Stream stream, WorkspaceItemType type)
        {
            Name = name;
            OriginalName = Name;
            ItemType = type;
            Stream = stream;
            Children = new List<WorkspaceItem>(0);
        }

        public override string ToString()
        {
            return Name + (IsModified ? "*" : "");
        }
    }
}
