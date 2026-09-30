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
        Styles.Add(new FluentTheme());
        Styles.Add(new Style(selector => selector.Is<Button>())
        {
            Setters = { new Setter(Button.VerticalContentAlignmentProperty, VerticalAlignment.Center) }
        });
        Styles.Add(new Style(selector => selector.OfType<MenuItem>().Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters = { new Setter(Avalonia.Controls.Presenters.ContentPresenter.VerticalContentAlignmentProperty, VerticalAlignment.Center) }
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
            mainView.DataContext = viewModel;
            lifetime.MainView = mainView;
            exportService.StorageProvider = () => Avalonia.Controls.TopLevel.GetTopLevel(lifetime.MainView)?.StorageProvider;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
