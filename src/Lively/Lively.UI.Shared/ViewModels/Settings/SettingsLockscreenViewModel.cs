using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lively.Common.Services;
using Lively.Grpc.Client;
using Lively.Models;
using Lively.Models.Enums;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Lively.UI.Shared.ViewModels
{
    public partial class SettingsLockscreenViewModel : ObservableObject
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly IUserSettingsClient userSettings;
        private readonly IDispatcherService dispatcher;
        private readonly ICommandsClient commandsClient;

        public SettingsLockscreenViewModel(IUserSettingsClient userSettings,
            IDispatcherService dispatcher,
            ICommandsClient commandsClient)
        {
            this.userSettings = userSettings;
            this.dispatcher = dispatcher;
            this.commandsClient = commandsClient;

            IsLockscreenEnabled = userSettings.Settings.LockScreenEnabled;
            IsFadeIn = userSettings.Settings.LockScreenFadeIn;
            IsShowClock = userSettings.Settings.LockScreenShowClock;
            IsSyncWindowsLockImage = userSettings.Settings.LockScreenSyncWindowsLockImage;
            Volume = userSettings.Settings.LockScreenGlobalVolume;
            SelectedLockscreenWaitIndex = (int)userSettings.Settings.LockScreenIdleDelay;
        }

        private bool _isLockscreenEnabled;
        public bool IsLockscreenEnabled
        {
            get => _isLockscreenEnabled;
            set
            {
                if (userSettings.Settings.LockScreenEnabled != value)
                {
                    userSettings.Settings.LockScreenEnabled = value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _isLockscreenEnabled, value);
            }
        }

        private bool _isFadeIn;
        public bool IsFadeIn
        {
            get => _isFadeIn;
            set
            {
                if (userSettings.Settings.LockScreenFadeIn != value)
                {
                    userSettings.Settings.LockScreenFadeIn = value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _isFadeIn, value);
            }
        }

        private bool _isShowClock;
        public bool IsShowClock
        {
            get => _isShowClock;
            set
            {
                if (userSettings.Settings.LockScreenShowClock != value)
                {
                    userSettings.Settings.LockScreenShowClock = value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _isShowClock, value);
            }
        }

        private bool _isSyncWindowsLockImage;
        public bool IsSyncWindowsLockImage
        {
            get => _isSyncWindowsLockImage;
            set
            {
                if (userSettings.Settings.LockScreenSyncWindowsLockImage != value)
                {
                    userSettings.Settings.LockScreenSyncWindowsLockImage = value;
                    UpdateSettingsConfigFile();
                    if (value)
                    {
                        _ = commandsClient.SyncLockscreenImage();
                    }
                }
                SetProperty(ref _isSyncWindowsLockImage, value);
            }
        }

        private int _volume;
        public int Volume
        {
            get => _volume;
            set
            {
                if (userSettings.Settings.LockScreenGlobalVolume != value)
                {
                    userSettings.Settings.LockScreenGlobalVolume = value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _volume, value);
            }
        }

        private int _selectedLockscreenWaitIndex;
        public int SelectedLockscreenWaitIndex
        {
            get => _selectedLockscreenWaitIndex;
            set
            {
                if (userSettings.Settings.LockScreenIdleDelay != (ScreensaverIdleTime)value)
                {
                    userSettings.Settings.LockScreenIdleDelay = (ScreensaverIdleTime)value;
                    UpdateSettingsConfigFile();
                }
                SetProperty(ref _selectedLockscreenWaitIndex, value);
            }
        }

        [RelayCommand]
        private void OpenWindowsSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = "ms-settings:lockscreen",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Could not launch Windows Lock Screen Settings: {ex}");
            }
        }

        [RelayCommand]
        private async Task PreviewLockscreen()
        {
            try
            {
                await commandsClient.ShowLockscreen(userSettings.Settings.LockScreenFadeIn);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error triggering lockscreen preview: {ex}");
            }
        }

        private void UpdateSettingsConfigFile()
        {
            dispatcher.TryEnqueue(userSettings.Save<SettingsModel>);
        }
    }
}
