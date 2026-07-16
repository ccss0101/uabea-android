using AssetsTools.NET;
using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.Messaging.Messages;
using System.Collections.Generic;

namespace UABEAvalonia
{
    /// <summary>
    /// 跨 ViewModel 通信的消息类型集合，基于 CommunityToolkit WeakReferenceMessenger。
    /// 解耦 Workspace、Inspector、Previewer、Hierarchy 等组件，避免直接引用。
    /// 参考自 UABEANext 的 Messages.cs。
    /// </summary>

    /// <summary>选中的资产集合发生变化。</summary>
    public class AssetsSelectedMessage : ValueChangedMessage<List<AssetContainer>>
    {
        public AssetsSelectedMessage(List<AssetContainer> value) : base(value) { }
    }

    /// <summary>请求打开编辑数据窗口。</summary>
    public class RequestEditAssetMessage : ValueChangedMessage<AssetContainer>
    {
        public RequestEditAssetMessage(AssetContainer value) : base(value) { }
    }

    /// <summary>请求跳转到指定资产（按 FileID + PathID）。</summary>
    public class RequestVisitAssetMessage : ValueChangedMessage<AssetPPtr>
    {
        public RequestVisitAssetMessage(AssetPPtr value) : base(value) { }
    }

    /// <summary>请求打开场景层级视图，并定位到指定 GameObject。</summary>
    public class RequestSceneViewMessage : ValueChangedMessage<AssetContainer>
    {
        public RequestSceneViewMessage(AssetContainer value) : base(value) { }
    }

    /// <summary>资产被修改/新增/删除，通知 Inspector、Previewer 等刷新。</summary>
    public class AssetsUpdatedMessage : ValueChangedMessage<List<AssetContainer>>
    {
        public AssetsUpdatedMessage(List<AssetContainer> value) : base(value) { }
    }

    /// <summary>工作区选中项发生变化（文件树节点）。</summary>
    public class SelectedWorkspaceItemChangedMessage : ValueChangedMessage<object?>
    {
        public SelectedWorkspaceItemChangedMessage(object? value) : base(value) { }
    }

    /// <summary>请求关闭指定文件/文档。</summary>
    public class RequestCloseFileMessage : ValueChangedMessage<string>
    {
        public RequestCloseFileMessage(string value) : base(value) { }
    }

    /// <summary>工作区正在关闭，通知各组件释放资源。</summary>
    public class WorkspaceClosingMessage : ValueChangedMessage<bool>
    {
        public WorkspaceClosingMessage(bool value) : base(value) { }
    }
}
