using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Views;
using AndroidX.Activity;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using TbkReport.Mobile.ViewModels;
using TbkReport.Mobile.Views;

namespace TbkReport.Mobile;

[Activity(Label = "ТБК Отчёт", MainLauncher = true,
    Theme = "@style/Theme.AppCompat.NoActionBar",
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    public static event Action<bool, double>? KeyboardVisibilityChanged;

    private SystemBackPressedCallback? systemBackPressedCallback;
    private KeyboardLayoutListener? keyboardLayoutListener;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        systemBackPressedCallback = new SystemBackPressedCallback(this);
        OnBackPressedDispatcher.AddCallback(this, systemBackPressedCallback);
        if (Window?.DecorView is { } decorView)
        {
            keyboardLayoutListener = new KeyboardLayoutListener(decorView);
            decorView.ViewTreeObserver?.AddOnGlobalLayoutListener(keyboardLayoutListener);
        }
    }

    protected override void OnDestroy()
    {
        if (keyboardLayoutListener is not null && Window?.DecorView.ViewTreeObserver is { } observer)
            observer.RemoveOnGlobalLayoutListener(keyboardLayoutListener);
        keyboardLayoutListener = null;
        base.OnDestroy();
    }

    private const int ExitPressWindowMilliseconds = 3_000;
    private int remainingExitPresses = 3;
    private long exitPressDeadline;

    private void HandleSystemBack()
    {
        if (ClockTimePicker.TryCancelActiveEdit())
        {
            ResetExitPresses();
            return;
        }
        if (TryHandleNavigationBack())
        {
            ResetExitPresses();
            return;
        }

        var now = global::Android.OS.SystemClock.ElapsedRealtime();
        if (now > exitPressDeadline) remainingExitPresses = 3;

        remainingExitPresses--;
        if (remainingExitPresses <= 0)
        {
            ResetExitPresses();
            Finish();
            return;
        }

        exitPressDeadline = now + ExitPressWindowMilliseconds;
        var suffix = remainingExitPresses == 1 ? "раз" : "раза";
        ShowExitNotification($"Нажмите ещё {remainingExitPresses} {suffix}, чтобы выйти из приложения");
    }

    private sealed class SystemBackPressedCallback(MainActivity activity) : OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed() => activity.HandleSystemBack();
    }

    private sealed class KeyboardLayoutListener(global::Android.Views.View decorView) : Java.Lang.Object,
        ViewTreeObserver.IOnGlobalLayoutListener
    {
        private bool? wasVisible;
        private double previousHeight;

        public void OnGlobalLayout()
        {
            var visibleArea = new global::Android.Graphics.Rect();
            decorView.GetWindowVisibleDisplayFrame(visibleArea);
            var rootHeight = decorView.RootView?.Height ?? decorView.Height;
            var obscuredPixels = Math.Max(0, rootHeight - visibleArea.Bottom);
            var isVisible = rootHeight > 0 && obscuredPixels > rootHeight * 0.15;
            var density = decorView.Resources?.DisplayMetrics?.Density ?? 1f;
            var keyboardHeight = isVisible ? obscuredPixels / Math.Max(0.1f, density) : 0;
            if (wasVisible == isVisible && Math.Abs(previousHeight - keyboardHeight) < 8) return;
            wasVisible = isVisible;
            previousHeight = keyboardHeight;
            KeyboardVisibilityChanged?.Invoke(isVisible, keyboardHeight);
        }
    }

    private static bool TryHandleNavigationBack()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not ISingleViewApplicationLifetime lifetime)
            return false;
        return lifetime.MainView?.DataContext is MainViewModel viewModel && viewModel.HandleBack();
    }

    private static void ShowExitNotification(string message)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime lifetime &&
            lifetime.MainView?.DataContext is MainViewModel viewModel)
        {
            viewModel.ShowExitNotification(message);
        }
    }

    private void ResetExitPresses()
    {
        remainingExitPresses = 3;
        exitPressDeadline = 0;
    }
}

[global::Android.App.Application(Icon = "@mipmap/tbk_app_icon")]
public sealed class AndroidApplication(IntPtr handle, JniHandleOwnership ownership)
    : AvaloniaAndroidApplication<App>(handle, ownership)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
