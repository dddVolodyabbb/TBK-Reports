using Android.App;
using Android.Content.PM;
using Android.Runtime;
using System;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace TbkReport.OfflineCheck;

// Test harness for offline toolchain validation, not the Android report application.
[Activity(Label = "TBK Offline Check", MainLauncher = true,
    Theme = "@style/Theme.AppCompat.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class OfflineSmokeActivity : AvaloniaMainActivity
{
}

[Android.App.Application]
public sealed class OfflineSmokeAndroidApplication(IntPtr handle, JniHandleOwnership ownership)
    : AvaloniaAndroidApplication<OfflineSmokeApp>(handle, ownership)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}

public sealed class OfflineSmokeApp : Avalonia.Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
            lifetime.MainView = new TextBlock { Text = "TBK: offline build check" };
        base.OnFrameworkInitializationCompleted();
    }
}
