global using System;
global using System.Collections.Generic;
global using System.Collections.ObjectModel;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using Microsoft.UI.Xaml;
global using Microsoft.UI.Xaml.Controls;
global using Microsoft.UI.Xaml.Media;
global using Microsoft.UI.Xaml.Media.Imaging;
global using Microsoft.UI.Xaml.Input;
global using Windows.UI;
global using OppoPodsManager.Assets.Localization;
global using OppoPodsManager.Assets.UserSettings;
global using OppoPodsManager.Control.Abstractions;
global using OppoPodsManager.Control.Subsystems.Desktop;
global using OppoPodsManager.Control.Subsystems.Logging;
global using OppoPodsManager.Control.Brands.Oppo.Models;
global using OppoPodsManager.Control.Core.Models;
global using OppoPodsManager.Control.Core.Transport;
global using OppoPodsManager.Control.Core.Features;
global using OppoPodsManager.UI.Toast;
global using OppoPodsManager.UI.Views;
global using OppoPodsManager.UI.Controls;

namespace OppoPodsManager;

public static class AppDispatcher
{
    private static Microsoft.UI.Dispatching.DispatcherQueue? _queue;

    public static void Initialize(Microsoft.UI.Dispatching.DispatcherQueue queue)
    {
        _queue = queue;
    }

    public static Microsoft.UI.Dispatching.DispatcherQueue? Queue
        => _queue;

    public static void Post(Action action)
    {
        var q = Queue;
        if (q is null || !q.TryEnqueue(() => action()))
            ApplicationLog.Current?.Debug("Dispatcher", "UI 调度队列不可用，已忽略退出期间的回调。");
    }

    public static Task InvokeAsync(Func<Task> action)
    {
        var q = Queue;
        if (q == null)
            return Task.FromException(new InvalidOperationException("UI 调度队列尚未初始化。"));

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!q.TryEnqueue(async () =>
        {
            try
            {
                await action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }))
            tcs.TrySetException(new InvalidOperationException("UI 调度队列已停止。"));
        return tcs.Task;
    }

    public static Task InvokeAsync(Action action)
    {
        return InvokeAsync(() =>
        {
            action();
            return Task.CompletedTask;
        });
    }
}

public static class Dispatcher
{
    public static class UIThread
    {
        public static void Post(Action action) => AppDispatcher.Post(action);
        public static Task InvokeAsync(Func<Task> action) => AppDispatcher.InvokeAsync(action);
        public static Task InvokeAsync(Action action) => AppDispatcher.InvokeAsync(action);
    }
}
