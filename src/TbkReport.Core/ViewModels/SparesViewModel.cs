using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TbkReport.Services;

namespace TbkReport.ViewModels;

/// <summary>A row of the «В наличии» list: a catalogue part with its quantity in stock.</summary>
public sealed record SpareStockRow(string PartId, string Name, string Article, int Quantity);

/// <summary>
/// The «ЗИП» section: the parts catalogue («Комплектующие») and the stock («В наличии»).
/// Both lists work the same way: a table with «Изменить» / «Удалить» and an entry form below it.
/// Every change is written to the file at once.
/// </summary>
public partial class SparesViewModel : ViewModelBase
{
    private readonly SpareStore store;

    public ObservableCollection<SparePart> Parts { get; } = new();
    [ObservableProperty] private string partName = "";
    [ObservableProperty] private string partArticle = "";
    [ObservableProperty] private string partValidation = "";
    [ObservableProperty] private SparePart? editingPart;
    [ObservableProperty] private bool isPartNameInvalid;
    public bool IsEditingPart => EditingPart is not null;
    public bool HasParts => Parts.Count > 0;
    public string PartActionText => IsEditingPart ? "Сохранить изменения" : "Добавить комплектующую";

    public ObservableCollection<SpareStockRow> StockRows { get; } = new();
    /// <summary>Catalogue parts that are not in the stock list yet.</summary>
    public ObservableCollection<SparePart> StockChoices { get; } = new();
    [ObservableProperty] private SparePart? selectedStockPart;
    [ObservableProperty] private string stockQuantity = "";
    [ObservableProperty] private string stockValidation = "";
    [ObservableProperty] private SpareStockRow? editingStock;
    [ObservableProperty] private bool isStockPartInvalid;
    [ObservableProperty] private bool isStockQuantityInvalid;
    public bool IsEditingStock => EditingStock is not null;
    public bool IsAddingStock => !IsEditingStock;
    public bool HasStockRows => StockRows.Count > 0;
    public string EditingStockLabel => EditingStock is { } row
        ? string.IsNullOrEmpty(row.Article) ? row.Name : $"{row.Name} · {row.Article}" : "";
    public string StockActionText => IsEditingStock ? "Сохранить изменения" : "Добавить в наличие";
    public string StockHint => Parts.Count == 0
        ? "Справочник пуст. Сначала добавьте комплектующие на вкладке «Комплектующие»."
        : StockChoices.Count == 0 && !IsEditingStock
            ? "Все комплектующие из справочника уже есть в этом списке. Количество меняется кнопкой «Изменить»."
            : "";
    public bool HasStockHint => StockHint.Length > 0;

    public SparesViewModel(SpareStore store)
    {
        this.store = store;
        store.Changed += Refresh;
        try { Refresh(); }
        catch (Exception ex) { PartValidation = StockValidation = ex.Message; }
    }

    partial void OnPartNameChanged(string value) { IsPartNameInvalid = false; PartValidation = ""; }
    partial void OnPartArticleChanged(string value) => PartValidation = "";
    partial void OnEditingPartChanged(SparePart? value)
    {
        OnPropertyChanged(nameof(IsEditingPart));
        OnPropertyChanged(nameof(PartActionText));
    }
    partial void OnSelectedStockPartChanged(SparePart? value) { IsStockPartInvalid = false; if (value is not null) StockValidation = ""; }
    partial void OnStockQuantityChanged(string value) { IsStockQuantityInvalid = false; StockValidation = ""; }
    partial void OnEditingStockChanged(SpareStockRow? value)
    {
        OnPropertyChanged(nameof(IsEditingStock));
        OnPropertyChanged(nameof(IsAddingStock));
        OnPropertyChanged(nameof(EditingStockLabel));
        OnPropertyChanged(nameof(StockActionText));
        OnPropertyChanged(nameof(StockHint));
        OnPropertyChanged(nameof(HasStockHint));
    }

    private void Refresh()
    {
        var selectedId = SelectedStockPart?.Id;
        Parts.Clear();
        foreach (var part in store.Catalog.OrderBy(part => part.Name, StringComparer.CurrentCultureIgnoreCase)) Parts.Add(part);
        StockRows.Clear();
        foreach (var row in store.Stock
                     .Select(item => store.FindPart(item.PartId) is { } part ? new SpareStockRow(part.Id, part.Name, part.Article, item.Quantity) : null)
                     .Where(row => row is not null).OrderBy(row => row!.Name, StringComparer.CurrentCultureIgnoreCase))
            StockRows.Add(row!);
        StockChoices.Clear();
        foreach (var part in Parts.Where(part => StockRows.All(row => row.PartId != part.Id))) StockChoices.Add(part);
        SelectedStockPart = StockChoices.FirstOrDefault(part => part.Id == selectedId);
        OnPropertyChanged(nameof(HasParts));
        OnPropertyChanged(nameof(HasStockRows));
        OnPropertyChanged(nameof(StockHint));
        OnPropertyChanged(nameof(HasStockHint));
    }

    [RelayCommand]
    private void AddOrSavePart()
    {
        IsPartNameInvalid = string.IsNullOrWhiteSpace(PartName);
        if (IsPartNameInvalid)
        {
            PartValidation = "Введите наименование комплектующей.";
            return;
        }
        try
        {
            if (EditingPart is { } part) store.UpdatePart(part.Id, PartName, PartArticle);
            else store.AddPart(PartName, PartArticle);
            CancelPartEdit();
        }
        catch (Exception ex) { PartValidation = ex.Message; }
    }

    [RelayCommand]
    private void CancelPartEdit()
    {
        EditingPart = null; PartName = ""; PartArticle = ""; PartValidation = ""; IsPartNameInvalid = false;
    }

    public void BeginEditPart(SparePart part)
    {
        EditingPart = part; PartName = part.Name; PartArticle = part.Article; PartValidation = ""; IsPartNameInvalid = false;
    }

    public void RemovePart(SparePart part)
    {
        try
        {
            if (EditingPart?.Id == part.Id) CancelPartEdit();
            store.RemovePart(part.Id);
            PartValidation = "";
        }
        catch (Exception ex) { PartValidation = ex.Message; }
    }

    [RelayCommand]
    private void AddOrSaveStock()
    {
        var partId = EditingStock?.PartId ?? SelectedStockPart?.Id;
        IsStockPartInvalid = partId is null;
        IsStockQuantityInvalid = !int.TryParse(StockQuantity.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var quantity);
        if (IsStockPartInvalid || IsStockQuantityInvalid)
        {
            StockValidation = IsStockPartInvalid
                ? IsStockQuantityInvalid ? "Выберите комплектующую и введите количество." : "Выберите комплектующую."
                : "Количество должно быть целым неотрицательным числом.";
            return;
        }
        try
        {
            store.SetStock(partId!, quantity);
            CancelStockEdit();
        }
        catch (Exception ex) { StockValidation = ex.Message; }
    }

    [RelayCommand]
    private void CancelStockEdit()
    {
        EditingStock = null; SelectedStockPart = null; StockQuantity = ""; StockValidation = "";
        IsStockPartInvalid = IsStockQuantityInvalid = false;
    }

    public void BeginEditStock(SpareStockRow row)
    {
        EditingStock = row;
        StockQuantity = row.Quantity.ToString(CultureInfo.InvariantCulture);
        StockValidation = "";
        IsStockPartInvalid = IsStockQuantityInvalid = false;
    }

    public void RemoveStock(SpareStockRow row)
    {
        try
        {
            if (EditingStock?.PartId == row.PartId) CancelStockEdit();
            store.RemoveStock(row.PartId);
            StockValidation = "";
        }
        catch (Exception ex) { StockValidation = ex.Message; }
    }
}
