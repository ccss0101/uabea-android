using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Globalization;

namespace UABEA.Android
{
    public partial class GoToAssetView : UserControl
    {
        public event EventHandler<long?>? Confirmed;

        public GoToAssetView()
        {
            InitializeComponent();
            btnConfirm.Click += BtnConfirm_Click;
            btnCancel.Click += BtnCancel_Click;
        }

        public void Reset()
        {
            boxPathId.Text = "";
            errorText.IsVisible = false;
            boxPathId.Focus();
        }

        private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
        {
            string text = boxPathId.Text ?? "";
            if (long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long pathId))
            {
                errorText.IsVisible = false;
                Confirmed?.Invoke(this, pathId);
            }
            else
            {
                errorText.Text = "请输入有效数字";
                errorText.IsVisible = true;
            }
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            Confirmed?.Invoke(this, null);
        }
    }
}
