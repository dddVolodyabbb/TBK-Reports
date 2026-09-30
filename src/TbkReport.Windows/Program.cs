using Avalonia;
using System;

namespace TbkReport;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    
    internal static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        using var instance = new SingleInstance();
        if (!instance.IsPrimary) return;
        Instance = instance;
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { Instance = null; }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseWin32()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .LogToTrace();
}


