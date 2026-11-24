using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using TbkReport.Mobile.ViewModels;
using TbkReport.Mobile.Views;

namespace TbkReport.Mobile;

public sealed class App : Application
{
    public override void Initialize()
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        Resources["StringTimePickerHourText"] = "часы";
        Resources["StringTimePickerMinuteText"] = "минуты";
        // Accent-coloured text readable on both backgrounds: darker blue in the light theme.
        Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
            {
                ["AccentTextBrush"] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF005A9E")),
                // Green for spare parts already in stock, readable on the light background.
                ["InStockTextBrush"] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF1B7F2A"))
            };
        Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
            {
                ["AccentTextBrush"] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF42A5F5")),
                ["InStockTextBrush"] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF66BB6A"))
            };
        Styles.Add(new FluentTheme());
        Styles.Add(new Style(selector => selector.Is<Button>())
        {
            Setters = { new Setter(Button.VerticalContentAlignmentProperty, VerticalAlignment.Center) }
        });
        Styles.Add(new Style(selector => selector.OfType<MenuItem>().Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters = { new Setter(Avalonia.Controls.Presenters.ContentPresenter.VerticalContentAlignmentProperty, VerticalAlignment.Center) }
        });
        // «Отправить на почту» stays tappable when unavailable, so a tap can explain why; it only looks grey.
        Styles.Add(new Style(selector => selector.OfType<MenuItem>().Class("MailUnavailable"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.45) }
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
        {
            var context = global::Android.App.Application.Context;
            var paths = new Common(context);
            paths.EnsureTemplate(context);
            var exportService = new AndroidExportService(context, paths);
            var mainView = new MainView();
            var viewModel = new MainViewModel(paths, Common.DefaultExportLocation, paths.TemplatePath, exportService);
            ApplyTheme(viewModel.Report.Theme);
            viewModel.Report.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TbkReport.ViewModels.ReportViewModel.Theme)) ApplyTheme(viewModel.Report.Theme);
            };
            mainView.DataContext = viewModel;
            lifetime.MainView = mainView;
            exportService.StorageProvider = () => Avalonia.Controls.TopLevel.GetTopLevel(lifetime.MainView)?.StorageProvider;
        }
        base.OnFrameworkInitializationCompleted();
    }

    // «Как в системе» is Avalonia's Default variant, which follows the Android light/dark setting.
    private void ApplyTheme(TbkReport.Services.AppTheme theme) => RequestedThemeVariant = theme switch
    {
        TbkReport.Services.AppTheme.Light => ThemeVariant.Light,
        TbkReport.Services.AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };
}
