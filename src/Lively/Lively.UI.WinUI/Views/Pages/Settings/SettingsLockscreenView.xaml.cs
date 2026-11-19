using Lively.UI.Shared.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Lively.UI.WinUI.Views.Pages.Settings
{
    public sealed partial class SettingsLockscreenView : Page
    {
        public SettingsLockscreenViewModel ViewModel { get; }

        public SettingsLockscreenView()
        {
            this.InitializeComponent();
            this.ViewModel = App.Services.GetRequiredService<SettingsLockscreenViewModel>();
            this.DataContext = ViewModel;
        }
    }
}
