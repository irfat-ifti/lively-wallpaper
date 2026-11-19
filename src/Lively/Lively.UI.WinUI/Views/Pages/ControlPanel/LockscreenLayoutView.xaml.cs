using Lively.UI.Shared.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Lively.UI.WinUI.Views.Pages.ControlPanel
{
    public sealed partial class LockscreenLayoutView : Page
    {
        private LockscreenLayoutViewModel viewModel;

        public LockscreenLayoutView()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            this.viewModel ??= (DataContext as ControlPanelViewModel)?.LockscreenVm;
            this.DataContext = this.viewModel;
        }
    }
}
