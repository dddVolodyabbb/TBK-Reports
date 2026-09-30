using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TbkReport.ViewModels;
using TbkReport.Views;

namespace TbkReport;

public partial class App : Application
{
    public override void Initialize()
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            var activationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            activationTimer.Tick += (_, _) =>
            {
                if (Program.Instance?.TakeActivationRequest() != true) return;
                var window = lifetime.MainWindow;
                if (window is null) return;
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Show();
                window.Activate();
            };
            lifetime.Exit += (_, _) => activationTimer.Stop();
            activationTimer.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
