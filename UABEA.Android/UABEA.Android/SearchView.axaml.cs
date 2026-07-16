using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using UABEAvalonia;

namespace UABEA.Android
{
    public partial class SearchView : UserControl
    {
        public event EventHandler<SearchDialogResult?>? Confirmed;

        public SearchView()
        {
            InitializeComponent();
            btnConfirm.Click += BtnConfirm_Click;
            btnCancel.Click += BtnCancel_Click;
        }

        public void FocusKeyword()
        {
            boxKeyword.Focus();
            boxKeyword.SelectAll();
        }

        private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
        {
            string text = boxKeyword.Text ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                boxKeyword.Focus();
                return;
            }
            var result = new SearchDialogResult(
                true,
                text,
                rdoDown.IsChecked ?? true,
                chkCaseSensitive.IsChecked ?? false);
            Confirmed?.Invoke(this, result);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            Confirmed?.Invoke(this, null);
        }
    }
}
