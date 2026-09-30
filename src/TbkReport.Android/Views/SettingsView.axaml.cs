using Avalonia.Controls;

namespace TbkReport.Mobile.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private TbkReport.ViewModels.DataTransferViewModel? Transfer =>
        DataContext is TbkReport.Mobile.ViewModels.MainViewModel vm ? vm.Report.DataTransfer : null;

    private static Avalonia.Platform.Storage.FilePickerFileType TransferFileType => new("Данные ТБК Отчёт")
    {
        Patterns = new[] { "*.tbkdata" },
        MimeTypes = new[] { "application/octet-stream", "application/json" }
    };

    private async void ExportDataClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Transfer is not { } transfer || DataContext is not TbkReport.Mobile.ViewModels.MainViewModel viewModel) return;
        try
        {
            var bytes = transfer.Export();
            await viewModel.ExportService.ExportBackupAsync(bytes, viewModel.Report.ExportDirectory);
            transfer.Exported();
        }
        catch (System.Exception error) { transfer.ShowError(error); }
    }

    private async void ImportDataClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Transfer is not { } transfer || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Импорт данных", AllowMultiple = false,
                FileTypeFilter = new[] { TransferFileType, Avalonia.Platform.Storage.FilePickerFileTypes.All }
            });
            if (files.Count == 0) return;
            using var file = files[0];
            await using var input = await file.OpenReadAsync();
            using var buffer = new System.IO.MemoryStream();
            var chunk = new byte[81920];
            int count;
            while ((count = await input.ReadAsync(chunk)) != 0)
            {
                if (buffer.Length + count > 64 * 1024 * 1024)
                    throw new System.IO.InvalidDataException("Файл данных превышает 64 МБ.");
                buffer.Write(chunk, 0, count);
            }
            transfer.PrepareImport(buffer.ToArray());
        }
        catch (System.Exception error) { transfer.ShowError(error); }
    }
}
