using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.ApplicationModel;
using OppoPodsManager.Control.Subsystems.Logging;

namespace OppoPodsManager.Assets.UserSettings;

public sealed record StartupStatus(bool Enabled, bool CanChange, string? MessageKey = null, bool Succeeded = true);

public static class WindowsStartup
{
    public const string TaskId = "CurryPodsStartup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CurryPods";
    private const string LegacyValueName = "OppoPodsManager";

    public static Task<StartupStatus> GetStatusAsync() => ApplyAsync(null);
    public static Task<StartupStatus> SetEnabledAsync(bool enabled) => ApplyAsync(enabled);

    private static async Task<StartupStatus> ApplyAsync(bool? enabled)
    {
        try
        {
            if (HasPackageIdentity())
            {
                var task = await StartupTask.GetAsync(TaskId);
                var state = task.State;
                if (enabled == true && state == StartupTaskState.Disabled)
                    state = await task.RequestEnableAsync();
                else if (enabled == false && state == StartupTaskState.Enabled)
                {
                    task.Disable();
                    state = task.State;
                }
                // Packaged apps must use the declared task, not a version-specific executable path.
                RemoveLegacyRunEntries();
                ApplicationLog.Current?.Info("Startup", $"Windows 启动任务：requested={enabled?.ToString() ?? "read"}，state={state}。");
                return state switch
                {
                    StartupTaskState.Enabled => new(true, true),
                    StartupTaskState.Disabled => new(false, true),
                    StartupTaskState.DisabledByUser => new(false, false, "Settings_StartupDisabledByUser"),
                    StartupTaskState.EnabledByPolicy => new(true, false, "Settings_StartupManaged"),
                    _ => new(false, false, "Settings_StartupManaged")
                };
            }

            // Retain support for an unpackaged executable; create the Run key if it does not exist.
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !string.Equals(Path.GetFileName(executable), "CurryPods.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("无法确定 CurryPods 可执行文件路径。");
            var command = $"\"{executable}\"";
            if (enabled is { } requested)
            {
                if (requested) key.SetValue(ValueName, command, RegistryValueKind.String);
                else key.DeleteValue(ValueName, throwOnMissingValue: false);
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            }
            return new(string.Equals(key.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase), true);
        }
        catch (Exception exception)
        {
            ApplicationLog.Current?.Error("Startup", "读取或更新 Windows 开机启动失败。", exception);
            Trace.TraceError("读取或更新 Windows 开机启动失败：" + exception);
            return new(false, true, "Settings_StartupFailed", Succeeded: false);
        }
    }

    private static void RemoveLegacyRunEntries()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        catch (Exception exception)
        {
            // Failure to remove an obsolete entry must not hide the native task's actual state.
            ApplicationLog.Current?.Error("Startup", "清理旧版开机启动项失败。", exception);
        }
    }

    private static bool HasPackageIdentity()
    {
        uint length = 0;
        var result = GetCurrentPackageFullName(ref length, nint.Zero);
        return result switch
        {
            0 or 122 => true, // ERROR_SUCCESS / ERROR_INSUFFICIENT_BUFFER
            15700 => false, // APPMODEL_ERROR_NO_PACKAGE
            _ => throw new System.ComponentModel.Win32Exception(result)
        };
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, nint packageFullName);
}
