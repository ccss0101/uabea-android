using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace UABEA.Android
{
    public partial class EditDataView : UserControl
    {
        public event EventHandler<string?>? Confirmed;

        public EditDataView()
        {
            InitializeComponent();
            btnConfirm.Click += BtnConfirm_Click;
            btnCancel.Click += BtnCancel_Click;
            TryInstallHighlighting();
        }

        public void Initialize(string initialText)
        {
            editor.Text = initialText ?? string.Empty;
            try { editor.Focus(); } catch { }
        }

        private void TryInstallHighlighting()
        {
            try
            {
                var registryOptions = new UtxtRegistryOptions(ThemeName.DarkPlus);
                var installation = editor.InstallTextMate(registryOptions);
                installation.SetGrammar("source.utxt");
            }
            catch
            {
                // 语法高亮可选,失败则退化为纯文本编辑器
            }
        }

        private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
        {
            Confirmed?.Invoke(this, editor.Text);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e)
        {
            Confirmed?.Invoke(this, null);
        }
    }

    internal sealed class UtxtRegistryOptions : IRegistryOptions
    {
        private readonly RegistryOptions _inner;

        public UtxtRegistryOptions(ThemeName theme)
        {
            _inner = new RegistryOptions(theme);
        }

        public IRawTheme GetDefaultTheme() => _inner.GetDefaultTheme();

        public IRawTheme GetTheme(string scopeName) => _inner.GetTheme(scopeName);

        public ICollection<string> GetInjections(string scopeName) => _inner.GetInjections(scopeName);

        public IRawGrammar GetGrammar(string scopeName)
        {
            if (scopeName == "source.utxt")
            {
                return LoadUtxtGrammar();
            }
            return _inner.GetGrammar(scopeName);
        }

        private static IRawGrammar LoadUtxtGrammar()
        {
            Assembly assembly = typeof(UtxtRegistryOptions).Assembly;
            Stream? stream = null;
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith("utxt.tmLanguage.json", StringComparison.Ordinal))
                {
                    stream = assembly.GetManifestResourceStream(name);
                    if (stream != null) break;
                }
            }
            if (stream == null)
                throw new FileNotFoundException("utxt grammar resource not found");

            using (stream)
            using (var reader = new StreamReader(stream))
            {
                return GrammarReader.ReadGrammarSync(reader);
            }
        }
    }
}
