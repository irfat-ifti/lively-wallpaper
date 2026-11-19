using Lively.Common.Helpers;
using Lively.Common.Helpers.Pinvoke;
using Lively.Common.Services;
using Lively.Core;
using Lively.Extensions;
using Lively.Factories;
using Lively.Models;
using Lively.Models.Enums;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Lively.Views
{
    public partial class LockScreenWindow : Window
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly LibraryModel model;
        private readonly DisplayMonitor display;
        private readonly WallpaperArrangement arrangement;
        private readonly bool showClock;
        private readonly TaskCompletionSource loadingTaskCompletionSource = new();
        private IWallpaper wallpaper;
        private bool _isInitialized = false;
        private readonly DispatcherTimer clockTimer;

        private readonly IWallpaperPluginFactory wallpaperFactory;
        private readonly IUserSettingsService userSettings;

        public event EventHandler UnlockRequested;

        public LockScreenWindow(LibraryModel model,
            DisplayMonitor display,
            WallpaperArrangement arrangement,
            bool showClock = true,
            bool isAutoLoad = true)
        {
            userSettings = App.Services.GetRequiredService<IUserSettingsService>();
            wallpaperFactory = App.Services.GetRequiredService<IWallpaperPluginFactory>();
            this.model = model;
            this.display = display;
            this.arrangement = arrangement;
            this.showClock = showClock;

            InitializeComponent();
            this.Title = $"Lively Lock Screen - {model?.Title ?? "Default"}";

            if (!showClock)
            {
                OverlayGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                UpdateClock();
                clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                clockTimer.Tick += (s, e) => UpdateClock();
                clockTimer.Start();
            }

            this.KeyDown += (s, e) => UnlockRequested?.Invoke(this, EventArgs.Empty);
            this.MouseDown += (s, e) => UnlockRequested?.Invoke(this, EventArgs.Empty);

            if (isAutoLoad && model != null)
                _ = LoadWallpaperAsync();
        }

        private void UpdateClock()
        {
            TimeTextBlock.Text = DateTime.Now.ToString("t");
            DateTextBlock.Text = DateTime.Now.ToString("dddd, MMMM d");
        }

        public async Task LoadWallpaperAsync()
        {
            if (_isInitialized || model == null)
                return;

            try
            {
                await loadingTaskCompletionSource.Task;
                wallpaper = wallpaperFactory.CreateWallpaper(model,
                    display,
                    arrangement,
                    isWindowed: true);
                await wallpaper.ShowAsync();

                this.SetProgramToFramework(wallpaper.Handle, PreviewBorder);
                this.Width += 1;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to load lock screen wallpaper: {e}");
            }
            finally
            {
                _isInitialized = true;
            }
        }

        public void SetWallpaperVolume(int volume)
        {
            wallpaper?.SetVolume(volume);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            loadingTaskCompletionSource.TrySetResult();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (wallpaper is null)
                return;

            var item = PreviewBorder.GetAbsolutePlacement(true);
            NativeMethods.POINT pts = new NativeMethods.POINT() { X = (int)item.Left, Y = (int)item.Top };
            if (NativeMethods.ScreenToClient(new WindowInteropHelper(this).Handle, ref pts))
            {
                NativeMethods.SetWindowPos(wallpaper.Handle, 1, pts.X, pts.Y, (int)item.Width, (int)item.Height, 0 | 0x0010);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            clockTimer?.Stop();
            try
            {
                wallpaper?.Close();
            }
            catch (Exception ex)
            {
                Logger.Error($"Error closing lockscreen wallpaper: {ex}");
            }
        }
    }
}
