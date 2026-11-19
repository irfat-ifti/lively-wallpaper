using System;
using System.Threading.Tasks;

namespace Lively.Common.Services
{
    public interface ILockscreenService
    {
        bool IsRunning { get; }

        Task StartAsync(bool isFadeIn);
        Task StopAsync();
        Task SyncLockScreenImageAsync();

        event EventHandler Started;
        event EventHandler Stopped;
    }
}
