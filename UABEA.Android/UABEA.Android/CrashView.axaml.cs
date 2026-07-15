using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UABEA.Android
{
    public partial class CrashView : UserControl
    {
        public CrashView()
        {
            InitializeComponent();
            logBox.Text = CrashLogger.ReadAll();
        }
    }
}
