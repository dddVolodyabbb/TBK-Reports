using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TbkReport.ViewModels;

namespace TbkReport.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private TbkReport.ViewModels.DataTransferViewModel? Transfer =>
        DataContext is TbkReport.ViewModels.MainViewModel vm ? vm.DataTransfer : null;

    private static Avalonia.Platform.Storage.FilePickerFileType TransferFileType => new("Данные ТБК Отчёт")
    {
        Patterns = new[] { "*.tbkdata" },
        MimeTypes = new[] { "application/octet-stream", "application/json" }
    };

    private async void ExportDataClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Transfer is not { } transfer || DataContext is not MainViewModel viewModel) return;
        try
        {
            var bytes = transfer.Export();
            var directory = Common.ResolveExportLocation(viewModel.ExportDirectory);
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "TBK Backup.tbkdata");
            await System.IO.File.WriteAllBytesAsync(path, bytes);
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

    private async void BrowseFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || TopLevel.GetTopLevel(this) is not { } topLevel) return;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Выберите папку для экспорта",
            AllowMultiple = false
        });
        if (folders.FirstOrDefault() is { } folder)
            viewModel.ExportDirectory = folder.Path.LocalPath;
    }

    private void ResetExportFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.ExportDirectory = Common.DefaultExportDirectory;
    }
}
