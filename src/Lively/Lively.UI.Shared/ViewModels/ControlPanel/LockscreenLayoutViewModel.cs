using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lively.Common;
using Lively.Common.Factories;
using Lively.Common.Helpers;
using Lively.Common.Helpers.Storage;
using Lively.Common.Services;
using Lively.Grpc.Client;
using Lively.Models;
using Lively.Models.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Lively.UI.Shared.ViewModels
{
    public partial class LockscreenLayoutViewModel : ObservableObject
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly IUserSettingsClient userSettings;
        private readonly IDesktopCoreClient desktopCore;
        private readonly IDisplayManagerClient displayManager;
        private readonly IDispatcherService dispatcher;
        private readonly IWallpaperLibraryFactory wallpaperLibraryFactory;
        private readonly ICommandsClient commandsClient;
        private readonly LibraryViewModel libraryVm;

        private readonly List<LockScreenLayoutModel> lockScreenLayout;

        public LockscreenLayoutViewModel(IUserSettingsClient userSettings,
            IDesktopCoreClient desktopCore,
            IDisplayManagerClient displayManager,
            IWallpaperLibraryFactory wallpaperLibraryFactory,
            IDispatcherService dispatcher,
            ICommandsClient commandsClient,
            LibraryViewModel libraryVm)
        {
            this.wallpaperLibraryFactory = wallpaperLibraryFactory;
            this.displayManager = displayManager;
            this.userSettings = userSettings;
            this.desktopCore = desktopCore;
            this.libraryVm = libraryVm;
            this.dispatcher = dispatcher;
            this.commandsClient = commandsClient;

            SelectedLockscreenArrangementIndex = (int)userSettings.Settings.LockScreenArrangement;
            SelectedLockscreenTypeIndex = (int)userSettings.Settings.LockScreenType;
            SelectedDisplay = userSettings.Settings.SelectedDisplay;
            lockScreenLayout = GetLockscreenConfigFile();

            UpdateLayout();
            desktopCore.WallpaperChanged += DesktopCore_WallpaperChanged;
        }

        [ObservableProperty]
        private bool isHideDialog;

        [ObservableProperty]
        private bool isShowSettings;

        [RelayCommand]
        private void OpenSettings()
        {
            IsShowSettings = true;
        }

        [ObservableProperty]
        private DisplayMonitor selectedDisplay;

        [ObservableProperty]
        private ObservableCollection<ScreenLayoutModel> screenItems = [];

        private ScreenLayoutModel _selectedItem;
        public ScreenLayoutModel SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (value is null)
                    return;

                SetProperty(ref _selectedItem, value);
                RemoveLockscreenCommand.NotifyCanExecuteChanged();

                if (!SelectedDisplay.Equals(value.Screen))
                    SelectedDisplay = value.Screen;
            }
        }

        [ObservableProperty]
        private WallpaperArrangement selectedLockscreenArrangement;

        private int _selectedLockscreenArrangementIndex;
        public int SelectedLockscreenArrangementIndex
        {
            get => _selectedLockscreenArrangementIndex;
            set
            {
                if (userSettings.Settings.LockScreenArrangement != (WallpaperArrangement)value && value != -1)
                {
                    userSettings.Settings.LockScreenArrangement = (WallpaperArrangement)value;
                    UpdateSettingsConfigFile();

                    RemoveLockscreenCommand.NotifyCanExecuteChanged();
                    UpdateLayout();
                }
                SetProperty(ref _selectedLockscreenArrangementIndex, value);
                SelectedLockscreenArrangement = userSettings.Settings.LockScreenType != LockScreenType.wallpaper ?
                    (WallpaperArrangement)value : userSettings.Settings.WallpaperArrangement;
            }
        }

        private int _selectedLockscreenTypeIndex;
        public int SelectedLockscreenTypeIndex
        {
            get => _selectedLockscreenTypeIndex;
            set
            {
                if (userSettings.Settings.LockScreenType != (LockScreenType)value && value != -1)
                {
                    userSettings.Settings.LockScreenType = (LockScreenType)value;
                    UpdateSettingsConfigFile();

                    SelectedLockscreenArrangement = userSettings.Settings.LockScreenType != LockScreenType.wallpaper ?
                        userSettings.Settings.LockScreenArrangement : userSettings.Settings.WallpaperArrangement;

                    RemoveLockscreenCommand.NotifyCanExecuteChanged();
                    UpdateLayout();
                }
                SetProperty(ref _selectedLockscreenTypeIndex, value);
                IsLockscreenLayout = (LockScreenType)value != LockScreenType.wallpaper;
            }
        }

        [RelayCommand(CanExecute = nameof(IsLockscreenLayout))]
        private async Task AddLockscreen()
        {
            try
            {
                IsHideDialog = true;
                var model = await libraryVm.SelectItem();
                if (model is null)
                    return;

                var layout = userSettings.Settings.LockScreenArrangement switch
                {
                    WallpaperArrangement.per => lockScreenLayout.Find(x => x.Layout == WallpaperArrangement.per)?.Wallpapers?.Find(x => x.Display.Equals(SelectedItem.Screen)),
                    WallpaperArrangement.span => lockScreenLayout.Find(x => x.Layout == WallpaperArrangement.span)?.Wallpapers?.FirstOrDefault(),
                    WallpaperArrangement.duplicate => lockScreenLayout.Find(x => x.Layout == WallpaperArrangement.duplicate)?.Wallpapers?.FirstOrDefault(),
                    _ => throw new NotImplementedException(),
                };

                if (layout is not null)
                {
                    layout.LivelyInfoPath = model.LivelyInfoFolderPath;
                }
                else
                {
                    switch (userSettings.Settings.LockScreenArrangement)
                    {
                        case WallpaperArrangement.per:
                            {
                                var perLayout = lockScreenLayout.Find(x => x.Layout == WallpaperArrangement.per);
                                if (perLayout is null)
                                {
                                    lockScreenLayout.Add(new LockScreenLayoutModel()
                                    {
                                        Layout = userSettings.Settings.LockScreenArrangement,
                                        Wallpapers =
                                        [
                                            new(SelectedItem.Screen, model.LivelyInfoFolderPath)
                                        ]
                                    });
                                }
                                else
                                {
                                    perLayout.Wallpapers ??= [];
                                    perLayout.Wallpapers.Add(new(SelectedItem.Screen, model.LivelyInfoFolderPath));
                                }
                            }
                            break;
                        case WallpaperArrangement.span:
                        case WallpaperArrangement.duplicate:
                            {
                                lockScreenLayout.Add(new LockScreenLayoutModel()
                                    {
                                        Layout = userSettings.Settings.LockScreenArrangement,
                                        Wallpapers =
                                        [
                                            new(SelectedItem.Screen, model.LivelyInfoFolderPath)
                                        ]
                                    });
                            }
                            break;
                    }
                }

                UpdateLockscreenConfigFile();
                RemoveLockscreenCommand.NotifyCanExecuteChanged();
                UpdateLayout();

                if (userSettings.Settings.LockScreenSyncWindowsLockImage)
                {
                    await commandsClient.SyncLockscreenImage();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting lock screen wallpaper: {ex}");
            }
            finally
            {
                IsHideDialog = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanRemoveLockscreen))]
        private void RemoveLockscreen()
        {
            switch (userSettings.Settings.LockScreenArrangement)
            {
                case WallpaperArrangement.per:
                    {
                        var item = lockScreenLayout.Find(x => x.Layout == WallpaperArrangement.per)?.Wallpapers?.Find(x => x.Display.Equals(SelectedItem.Screen));
                        if (item is not null)
                        {
                            lockScreenLayout.Find(x => x.Layout == WallpaperArrangement.per)?.Wallpapers?.Remove(item);
                        }
                        var screenItem = ScreenItems.FirstOrDefault(x => x.Screen.Equals(SelectedItem.Screen));
                        if (screenItem != null)
                            screenItem.ScreenImagePath = null;
                    }
                    break;
                case WallpaperArrangement.span:
                    {
                        lockScreenLayout.RemoveAll(x => x.Layout == WallpaperArrangement.span);
                        foreach (var item in ScreenItems)
                            item.ScreenImagePath = null;
                    }
                    break;
                case WallpaperArrangement.duplicate:
                    {
                        lockScreenLayout.RemoveAll(x => x.Layout == WallpaperArrangement.duplicate);
                        foreach (var item in ScreenItems)
                            item.ScreenImagePath = null;
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }
            UpdateLockscreenConfigFile();
            RemoveLockscreenCommand.NotifyCanExecuteChanged();
        }

        private bool CanRemoveLockscreen()
        {
            if (userSettings.Settings.LockScreenType != LockScreenType.different)
                return false;

            return userSettings.Settings.LockScreenArrangement switch
            {
                WallpaperArrangement.per => lockScreenLayout.Exists(x => x.Layout == WallpaperArrangement.per && x.Wallpapers != null && x.Wallpapers.Exists(x => x.Display.Equals(SelectedItem.Screen))),
                WallpaperArrangement.span => lockScreenLayout.Exists(x => x.Layout == WallpaperArrangement.span),
                WallpaperArrangement.duplicate => lockScreenLayout.Exists(x => x.Layout == WallpaperArrangement.duplicate),
                _ => throw new NotImplementedException(),
            };
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
                Logger.Error($"Error showing lockscreen preview: {ex}");
            }
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AddLockscreenCommand))]
        private bool isLockscreenLayout;

        public void OnWindowClosing()
        {
            desktopCore.WallpaperChanged -= DesktopCore_WallpaperChanged;
        }

        private void DesktopCore_WallpaperChanged(object sender, EventArgs e)
        {
            dispatcher.TryEnqueue(UpdateLayout);
        }

        private void UpdateLayout()
        {
            ScreenItems.Clear();
            switch (userSettings.Settings.LockScreenType)
            {
                case LockScreenType.wallpaper:
                    {
                        foreach (var item in displayManager.DisplayMonitors)
                        {
                            var wallpaper = userSettings.Settings.WallpaperArrangement switch
                            {
                                WallpaperArrangement.per => desktopCore.Wallpapers.FirstOrDefault(x => item.Equals(x.Display)),
                                WallpaperArrangement.span => desktopCore.Wallpapers.FirstOrDefault(),
                                WallpaperArrangement.duplicate => desktopCore.Wallpapers.FirstOrDefault(),
                                _ => throw new NotImplementedException(),
                            };
                            ScreenItems.Add(new ScreenLayoutModel(item,
                                string.IsNullOrEmpty(wallpaper?.PreviewPath) ? wallpaper?.ThumbnailPath : wallpaper.PreviewPath,
                                null,
                                string.Empty));
                        }
                    }
                    break;
                case LockScreenType.different:
                    {
                        foreach (var item in displayManager.DisplayMonitors)
                        {
                            var lockscreen = lockScreenLayout is null ? null : userSettings.Settings.LockScreenArrangement switch
                            {
                                WallpaperArrangement.per => lockScreenLayout.Find(x => x.Layout == WallpaperArrangement.per)?.Wallpapers?.Find(x => item.Equals(x.Display)),
                                WallpaperArrangement.span => lockScreenLayout.Find(x => x.Layout == WallpaperArrangement.span)?.Wallpapers?.FirstOrDefault(),
                                WallpaperArrangement.duplicate => lockScreenLayout.Find(x => x.Layout == WallpaperArrangement.duplicate)?.Wallpapers?.FirstOrDefault(),
                                _ => throw new NotImplementedException(),
                            };

                            LibraryModel wallpaper = null;
                            try
                            {
                                if (lockscreen is not null)
                                    wallpaper = wallpaperLibraryFactory.CreateFromDirectory(lockscreen.LivelyInfoPath);
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"Lock screen wallpaper {lockscreen?.LivelyInfoPath} loading failed: {ex}");
                                switch (userSettings.Settings.LockScreenArrangement)
                                {
                                    case WallpaperArrangement.per:
                                        if (lockscreen is not null)
                                            lockScreenLayout?.Find(x => x.Layout == WallpaperArrangement.per)?.Wallpapers?.Remove(lockscreen);
                                        break;
                                    case WallpaperArrangement.span:
                                        lockScreenLayout?.RemoveAll(x => x.Layout == WallpaperArrangement.span);
                                        break;
                                    case WallpaperArrangement.duplicate:
                                        lockScreenLayout?.RemoveAll(x => x.Layout == WallpaperArrangement.duplicate);
                                        break;
                                    default:
                                        throw new NotImplementedException();
                                }
                            }

                            ScreenItems.Add(new ScreenLayoutModel(item,
                                string.IsNullOrEmpty(wallpaper?.PreviewClipPath) ? wallpaper?.ThumbnailPath : wallpaper.PreviewClipPath,
                                null,
                                string.Empty));
                        }
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }

            if (ScreenItems.Count > 0)
            {
                var currentSelection = ScreenItems.FirstOrDefault(x => x.Screen.Equals(SelectedDisplay)) ?? ScreenItems[0];
                currentSelection.IsSelected = true;
                SelectedItem = currentSelection;
            }
        }

        private void UpdateSettingsConfigFile()
        {
            dispatcher.TryEnqueue(userSettings.Save<SettingsModel>);
        }

        private void UpdateLockscreenConfigFile()
        {
            try
            {
                JsonStorage<List<LockScreenLayoutModel>>.StoreData(Constants.CommonPaths.LockScreenLayoutPath, lockScreenLayout);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save LockScreen config file: {ex}");
            }
        }

        private List<LockScreenLayoutModel> GetLockscreenConfigFile()
        {
            try
            {
                if (File.Exists(Constants.CommonPaths.LockScreenLayoutPath))
                    return JsonStorage<List<LockScreenLayoutModel>>.LoadData(Constants.CommonPaths.LockScreenLayoutPath) ?? [];
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load LockScreen config: {ex}");
            }
            return [];
        }
    }
}
