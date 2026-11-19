using Lively.Common;
using Lively.Common.Factories;
using Lively.Common.Helpers;
using Lively.Common.Helpers.Pinvoke;
using Lively.Common.Helpers.Storage;
using Lively.Common.Services;
using Lively.Core.Display;
using Lively.Extensions;
using Lively.Models;
using Lively.Models.Enums;
using Lively.Views;
using Lively.Views.WindowMsg;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Lively.Services
{
    public class LockscreenService : ILockscreenService
    {
        public bool IsRunning { get; private set; } = false;

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly List<LockScreenWindow> lockscreenWindows = [];
        private readonly List<Window> blankWindows = [];
        private bool startAsyncExecuting, stopAsyncExecuting;

        private readonly IUserSettingsService userSettings;
        private readonly IDisplayManager displayManager;
        private readonly IWallpaperLibraryFactory wallpaperLibraryFactory;
        private readonly RawInputMsgWindow rawInput;

        public event EventHandler Started;
        public event EventHandler Stopped;

        public LockscreenService(IUserSettingsService userSettings,
            RawInputMsgWindow rawInput,
            IDisplayManager displayManager,
            IWallpaperLibraryFactory wallpaperLibraryFactory)
        {
            this.userSettings = userSettings;
            this.displayManager = displayManager;
            this.rawInput = rawInput;
            this.wallpaperLibraryFactory = wallpaperLibraryFactory;

            displayManager.DisplayUpdated += DisplayManager_DisplayUpdated;
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        }

        private async void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            try
            {
                if (e.Reason == SessionSwitchReason.SessionLock)
                {
                    Logger.Info("Workstation locked detected by LockscreenService.");
                    if (userSettings.Settings.LockScreenSyncWindowsLockImage)
                    {
                        _ = SyncLockScreenImageAsync();
                    }

                    if (userSettings.Settings.LockScreenEnabled && !IsRunning)
                    {
                        await StartAsync(userSettings.Settings.LockScreenFadeIn);
                    }
                }
                else if (e.Reason == SessionSwitchReason.SessionUnlock)
                {
                    Logger.Info("Workstation unlocked detected by LockscreenService.");
                    if (IsRunning)
                    {
                        await StopAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling SessionSwitch in LockscreenService: {ex}");
            }
        }

        private async void DisplayManager_DisplayUpdated(object sender, EventArgs e)
        {
            await StopAsync();
        }

        public async Task StartAsync(bool isFadeIn)
        {
            if (IsRunning || startAsyncExecuting || stopAsyncExecuting)
                return;

            startAsyncExecuting = true;
            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, async () =>
            {
                BlankWindow fadeInWindow = null;
                try
                {
                    if (isFadeIn)
                    {
                        Logger.Info("Showing lock screen fade transition...");
                        var tcs = new TaskCompletionSource<bool>();
                        fadeInWindow = new BlankWindow(fadeInDuration: 500, fadeOutDuration: 300)
                        {
                            WindowStartupLocation = WindowStartupLocation.CenterScreen,
                            BorderThickness = new Thickness(0),
                            ShowInTaskbar = false,
                            ShowActivated = false,
                            Topmost = true
                        };
                        fadeInWindow.Loaded += (s, e) =>
                        {
                            fadeInWindow.NativeResize(displayManager.VirtualScreenBounds);
                            fadeInWindow.WindowStyle = WindowStyle.None;
                            fadeInWindow.ResizeMode = ResizeMode.NoResize;
                        };
                        fadeInWindow.FadeInAnimationCompleted += (s, e) => tcs.TrySetResult(true);
                        fadeInWindow.Show();

                        void RawInput_UserInput(object sender, object e) => tcs.TrySetResult(false);
                        rawInput.MouseMoveRaw += RawInput_UserInput;
                        rawInput.MouseDownRaw += RawInput_UserInput;
                        rawInput.KeyboardClickRaw += RawInput_UserInput;

                        await tcs.Task;
                        rawInput.MouseMoveRaw -= RawInput_UserInput;
                        rawInput.MouseDownRaw -= RawInput_UserInput;
                        rawInput.KeyboardClickRaw -= RawInput_UserInput;

                        if (!tcs.Task.Result)
                            return;
                    }

                    IsRunning = true;
                    Logger.Info("Starting Live Lock Screen on all monitors...");
                    await ShowLockscreenWindows();
                    StartInputListener();
                    Started?.Invoke(this, EventArgs.Empty);
                }
                finally
                {
                    startAsyncExecuting = false;
                    fadeInWindow?.Close();
                }
            });
        }

        public async Task StopAsync()
        {
            if (!IsRunning || stopAsyncExecuting)
                return;

            stopAsyncExecuting = true;
            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                try
                {
                    Logger.Info("Stopping Live Lock Screen...");
                    StopInputListener();
                    CloseLockscreenWindows();
                    IsRunning = false;
                    Stopped?.Invoke(this, EventArgs.Empty);
                }
                finally
                {
                    stopAsyncExecuting = false;
                }
            });
        }

        private async Task ShowLockscreenWindows()
        {
            WallpaperArrangement arrangement = WallpaperArrangement.per;
            List<WallpaperLayoutModel> wallpaperLayout = null;

            switch (userSettings.Settings.LockScreenType)
            {
                case LockScreenType.wallpaper:
                    wallpaperLayout = userSettings.WallpaperLayout;
                    arrangement = userSettings.Settings.WallpaperArrangement;
                    break;
                case LockScreenType.different:
                    try
                    {
                        var layouts = JsonStorage<List<LockScreenLayoutModel>>.LoadData(Constants.CommonPaths.LockScreenLayoutPath);
                        arrangement = userSettings.Settings.LockScreenArrangement;
                        wallpaperLayout = layouts?.Find(x => x.Layout == arrangement)?.Wallpapers;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to read LockScreen config file: {ex}");
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }

            if (wallpaperLayout is null || wallpaperLayout.Count == 0)
            {
                ShowBlankWindow(displayManager.VirtualScreenBounds);
                return;
            }

            switch (arrangement)
            {
                case WallpaperArrangement.per:
                    {
                        foreach (var layout in wallpaperLayout)
                        {
                            try
                            {
                                var model = wallpaperLibraryFactory.CreateFromDirectory(layout.LivelyInfoPath);
                                var display = displayManager.DisplayMonitors.FirstOrDefault(x => x.Equals(layout.Display));
                                var volume = display?.IsPrimary == true ? userSettings.Settings.LockScreenGlobalVolume : 0;
                                bool showClock = display?.IsPrimary == true && userSettings.Settings.LockScreenShowClock;

                                if (display is null)
                                {
                                    Logger.Info($"Screen missing, skipping lockscreen {layout.LivelyInfoPath} | {layout.Display.DeviceName}");
                                }
                                else
                                {
                                    Logger.Info($"Starting lock screen on display {display.DeviceName} ({display.Bounds}) with wallpaper: {model.Title}");
                                    await ShowLockScreenWindow(model, display, volume, showClock);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Failed to load lock screen on display {layout.Display?.DeviceName}: {ex}");
                                ShowBlankWindow(layout.Display);
                            }
                        }

                        // Protect any monitor without wallpaper assigned
                        foreach (var display in displayManager.DisplayMonitors.Where(x => !wallpaperLayout.Exists(y => y.Display.Equals(x))))
                        {
                            ShowBlankWindow(display);
                        }
                    }
                    break;

                case WallpaperArrangement.span:
                    {
                        try
                        {
                            var model = wallpaperLibraryFactory.CreateFromDirectory(wallpaperLayout.FirstOrDefault()?.LivelyInfoPath);
                            await ShowLockScreenWindowSpan(model, displayManager.VirtualScreenBounds, userSettings.Settings.LockScreenGlobalVolume, userSettings.Settings.LockScreenShowClock);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to load spanned lock screen: {ex}");
                            ShowBlankWindow(displayManager.VirtualScreenBounds);
                        }
                    }
                    break;

                case WallpaperArrangement.duplicate:
                    {
                        try
                        {
                            var model = wallpaperLibraryFactory.CreateFromDirectory(wallpaperLayout.FirstOrDefault()?.LivelyInfoPath);
                            foreach (var display in displayManager.DisplayMonitors)
                            {
                                int volume = display.IsPrimary ? userSettings.Settings.LockScreenGlobalVolume : 0;
                                bool showClock = display.IsPrimary && userSettings.Settings.LockScreenShowClock;
                                await ShowLockScreenWindow(model, display, volume, showClock);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to duplicate lock screen: {ex}");
                            ShowBlankWindow(displayManager.VirtualScreenBounds);
                        }
                    }
                    break;

                default:
                    throw new NotImplementedException();
            }
        }

        private async Task ShowLockScreenWindow(LibraryModel model, DisplayMonitor display, int volume, bool showClock)
        {
            var window = new LockScreenWindow(
                model,
                display,
                userSettings.Settings.LockScreenArrangement,
                showClock: showClock,
                isAutoLoad: false)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                BorderThickness = new Thickness(0),
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
            };

            window.UnlockRequested += async (s, e) => await StopAsync();
            window.Show();
            window.NativeMove(display.Bounds);
            window.WindowState = WindowState.Maximized;
            await window.LoadWallpaperAsync();
            window.SetWallpaperVolume(volume);

            lockscreenWindows.Add(window);
        }

        private async Task ShowLockScreenWindowSpan(LibraryModel model, System.Drawing.Rectangle rect, int volume, bool showClock)
        {
            var window = new LockScreenWindow(
                model,
                displayManager.PrimaryDisplayMonitor,
                userSettings.Settings.LockScreenArrangement,
                showClock: showClock,
                isAutoLoad: false)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                BorderThickness = new Thickness(0),
                Topmost = true,
            };

            window.UnlockRequested += async (s, e) => await StopAsync();
            window.Show();
            window.NativeResize(rect);
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            await window.LoadWallpaperAsync();
            window.SetWallpaperVolume(volume);

            lockscreenWindows.Add(window);
        }

        private void CloseLockscreenWindows()
        {
            foreach (var win in lockscreenWindows)
            {
                try
                {
                    win.Close();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                }
            }
            lockscreenWindows.Clear();
            CloseBlankWindows();
        }

        private void ShowBlankWindow(DisplayMonitor display)
        {
            var window = new BlankWindow
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                BorderThickness = new Thickness(0),
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
            };
            window.Show();
            window.NativeMove(display.Bounds);
            window.WindowState = WindowState.Maximized;
            blankWindows.Add(window);
        }

        private void ShowBlankWindow(System.Drawing.Rectangle bounds)
        {
            var window = new BlankWindow
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                BorderThickness = new Thickness(0),
                Topmost = true,
            };
            window.Show();
            window.NativeResize(bounds);
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            blankWindows.Add(window);
        }

        private void CloseBlankWindows()
        {
            blankWindows.ForEach(x => x.Close());
            blankWindows.Clear();
        }

        private void StartInputListener()
        {
            rawInput.MouseMoveRaw += RawInput_InputReceived;
            rawInput.MouseDownRaw += RawInput_InputReceived;
            rawInput.KeyboardClickRaw += RawInput_InputReceived;
        }

        private void StopInputListener()
        {
            rawInput.MouseMoveRaw -= RawInput_InputReceived;
            rawInput.MouseDownRaw -= RawInput_InputReceived;
            rawInput.KeyboardClickRaw -= RawInput_InputReceived;
        }

        private async void RawInput_InputReceived(object sender, object e)
        {
            await StopAsync();
        }

        public async Task SyncLockScreenImageAsync()
        {
            await Task.Run(async () =>
            {
                try
                {
                    Directory.CreateDirectory(Constants.CommonPaths.TempDir);
                    string targetImagePath = Path.Combine(Constants.CommonPaths.TempDir, "lively_lockscreen.jpg");

                    var monitors = displayManager.DisplayMonitors.ToList();
                    var virtualBounds = displayManager.VirtualScreenBounds;

                    if (monitors.Count == 0 || virtualBounds.Width <= 0 || virtualBounds.Height <= 0)
                        return;

                    List<WallpaperLayoutModel> wallpaperLayout = null;
                    switch (userSettings.Settings.LockScreenType)
                    {
                        case LockScreenType.wallpaper:
                            wallpaperLayout = userSettings.WallpaperLayout;
                            break;
                        case LockScreenType.different:
                            var layouts = JsonStorage<List<LockScreenLayoutModel>>.LoadData(Constants.CommonPaths.LockScreenLayoutPath);
                            wallpaperLayout = layouts?.Find(x => x.Layout == userSettings.Settings.LockScreenArrangement)?.Wallpapers;
                            break;
                    }

                    if (wallpaperLayout is null || wallpaperLayout.Count == 0)
                        return;

                    // Composite image for multi-monitor / dual monitor layout
                    using var composite = new Bitmap(virtualBounds.Width, virtualBounds.Height);
                    using var g = Graphics.FromImage(composite);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(System.Drawing.Color.Black);

                    foreach (var monitor in monitors)
                    {
                        var layout = wallpaperLayout.Find(x => x.Display.Equals(monitor)) ?? wallpaperLayout.FirstOrDefault();
                        if (layout != null && !string.IsNullOrEmpty(layout.LivelyInfoPath))
                        {
                            var model = wallpaperLibraryFactory.CreateFromDirectory(layout.LivelyInfoPath);
                            string imgPath = !string.IsNullOrEmpty(model?.PreviewClipPath) && File.Exists(model.PreviewClipPath)
                                ? model.PreviewClipPath
                                : model?.ThumbnailPath;

                            if (!string.IsNullOrEmpty(imgPath) && File.Exists(imgPath))
                            {
                                using var srcImg = System.Drawing.Image.FromFile(imgPath);
                                int destX = monitor.Bounds.X - virtualBounds.X;
                                int destY = monitor.Bounds.Y - virtualBounds.Y;
                                g.DrawImage(srcImg, new System.Drawing.Rectangle(destX, destY, monitor.Bounds.Width, monitor.Bounds.Height));
                            }
                        }
                    }

                    composite.Save(targetImagePath, System.Drawing.Imaging.ImageFormat.Jpeg);

                    // Set Windows lock screen image via Windows.System.UserProfile.LockScreen
                    if (Windows.System.UserProfile.UserProfilePersonalizationSettings.IsSupported())
                    {
                        var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(targetImagePath);
                        await Windows.System.UserProfile.LockScreen.SetImageFileAsync(storageFile);
                        Logger.Info($"Successfully synchronized Windows lock screen image to: {targetImagePath}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to synchronize Windows lock screen image: {ex.Message}");
                }
            });
        }
    }
}
