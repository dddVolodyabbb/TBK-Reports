using Avalonia;
using GemBox.Spreadsheet;
using SkiaSharp;
using System.Reflection;

SpreadsheetInfo.SetLicense("FREE-LIMITED-KEY");
var workbook = new ExcelFile();
workbook.Worksheets.Add("Проверка").Cells["A1"].Value = "Avalonia + GemBox";
using var bitmap = new SKBitmap(8, 8);
bitmap.Erase(SKColors.SteelBlue);
using var pdf = new MemoryStream();
workbook.Save(pdf, new PdfSaveOptions { RenderingMode = RenderingMode.Skia });
var avaloniaSkia = Assembly.Load("Avalonia.Skia");
if (pdf.Length < 1_000 || bitmap.GetPixel(0, 0) != SKColors.SteelBlue)
    throw new InvalidOperationException("Проверка Skia или PDF не выполнена.");
Console.WriteLine($"Avalonia {typeof(Application).Assembly.GetName().Version}; Avalonia.Skia {avaloniaSkia.GetName().Version}; GemBox {typeof(ExcelFile).Assembly.GetName().Version}; Skia {typeof(SKBitmap).Assembly.GetName().Version}; PDF {pdf.Length} bytes");
