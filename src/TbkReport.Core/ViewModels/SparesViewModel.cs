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
/// A catalogue part offered when adding to «В наличии». A part already in stock is offered too, marked (green on
/// screen) with its quantity; choosing it updates that one stock row instead of adding a second one.
/// </summary>
public sealed record SpareStockChoice(SparePart Part, int? InStock)
{
    public string Id => Part.Id;
    public bool IsInStock => InStock is not null;
    public string Label => InStock is { } quantity ? $"{Part.Label} — в наличии {quantity} шт." : Part.Label;
}

/// <summary>
/// The «ЗИП» section: the parts catalogue («Комплектующие») and the stock («В наличии»).
/// Both lists work the same way: a search field, the list with «Изменить» / «Удалить», and an entry form that
/// opens with «Добавить» or «Изменить» and closes after saving or «Отмена». Every change is written to the file at once.
/// </summary>
public partial class SparesViewModel : ViewModelBase
{
    private readonly SpareStore store;

    /// <summary>Catalogue parts matching <see cref="PartsSearch"/>.</summary>
    public ObservableCollection<SparePart> Parts { get; } = new();
    [ObservableProperty] private string partsSearch = "";
    [ObservableProperty] private bool isPartFormOpen;
    [ObservableProperty] private string partName = "";
    [ObservableProperty] private string partArticle = "";
    [ObservableProperty] private string partManufacturer = "";
    /// <summary>Manufacturers already in the catalogue, offered while typing in the manufacturer field.</summary>
    public ObservableCollection<string> Manufacturers { get; } = new();
    [ObservableProperty] private string partValidation = "";
    [ObservableProperty] private SparePart? editingPart;
    [ObservableProperty] private bool isPartNameInvalid;
    public bool IsEditingPart => EditingPart is not null;
    public bool HasParts => Parts.Count > 0;
    public string PartActionText => IsEditingPart ? "Сохранить изменения" : "Добавить комплектующую";
    public string PartFormTitle => IsEditingPart ? "Изменение комплектующей" : "Новая комплектующая";
    public string PartsSummary => store.Catalog.Count == 0
        ? "Справочник пуст. Нажмите «Добавить»."
        : Parts.Count == 0 ? "Ничего не найдено."
        : Parts.Count == store.Catalog.Count ? $"Всего комплектующих: {store.Catalog.Count}"
        : $"Найдено: {Parts.Count} из {store.Catalog.Count}";

    /// <summary>Stock rows matching <see cref="StockSearch"/>.</summary>
    public ObservableCollection<SpareStockRow> StockRows { get; } = new();
    [ObservableProperty] private string stockSearch = "";
    [ObservableProperty] private bool isStockFormOpen;
    /// <summary>Catalogue parts matching <see cref="StockChoiceSearch"/>; those already in stock are marked.</summary>
    public ObservableCollection<SpareStockChoice> StockChoices { get; } = new();
    [ObservableProperty] private string stockChoiceSearch = "";
    [ObservableProperty] private SpareStockChoice? selectedStockPart;
    /// <summary>Shown under the list when the chosen part is already in stock.</summary>
    public string SelectedInStockText => SelectedStockPart?.InStock is { } quantity
        ? $"Уже в наличии: {quantity} шт. Новое количество заменит прежнее." : "";
    public bool IsSelectedInStock => SelectedStockPart?.IsInStock == true;
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
    public string StockFormTitle => IsEditingStock ? "Изменение количества" : "Добавление в наличие";
    public string StockSummary => store.Stock.Count == 0
        ? "Список пуст. Нажмите «Добавить»."
        : StockRows.Count == 0 ? "Ничего не найдено."
        : StockRows.Count == store.Stock.Count ? $"Всего позиций: {store.Stock.Count}"
        : $"Найдено: {StockRows.Count} из {store.Stock.Count}";
    public string StockHint => store.Catalog.Count == 0
        ? "Справочник пуст. Сначала добавьте комплектующие в разделе «Комплектующие»."
        : !IsEditingStock && StockChoices.Count == 0 ? "Ничего не найдено." : "";
    public bool HasStockHint => StockHint.Length > 0;

    /// <summary>«Остатки на дату»: the day «В наличии» was counted; usage of reports from that day on is taken from it.</summary>
    public DateTimeOffset? StockDate
    {
        get => new DateTimeOffset(store.StockDate);
        set
        {
            if (value is not { } date || date.Date == store.StockDate) return;
            try { store.SetStockDate(date.Date); StockValidation = ""; }
            catch (Exception ex) { StockValidation = ex.Message; }
        }
    }

    public bool IsAccountingEnabled => store.AccountingEnabled;
    public string AccountingText => store.AccountingEnabled
        ? "Учёт ЗИП включён: из количества вычитается расход из отчётов начиная с этой даты."
        : "Учёт ЗИП выключен: количество не уменьшается. Включается в «Параметрах» → «Учёт ЗИП».";

    public SparesViewModel(SpareStore store)
    {
        this.store = store;
        store.Changed += Refresh;
        try { Refresh(); }
        catch (Exception ex) { PartValidation = StockValidation = ex.Message; }
    }

    partial void OnPartsSearchChanged(string value) => Refresh();
    partial void OnStockSearchChanged(string value) => Refresh();
    partial void OnStockChoiceSearchChanged(string value) => Refresh();
    partial void OnPartNameChanged(string value) { IsPartNameInvalid = false; PartValidation = ""; }
    partial void OnPartArticleChanged(string value) => PartValidation = "";
    partial void OnEditingPartChanged(SparePart? value)
    {
        OnPropertyChanged(nameof(IsEditingPart));
        OnPropertyChanged(nameof(PartActionText));
        OnPropertyChanged(nameof(PartFormTitle));
    }
    partial void OnSelectedStockPartChanged(SpareStockChoice? value)
    {
        IsStockPartInvalid = false;
        if (value is not null) StockValidation = "";
        // A part already in stock starts from its current quantity, as if «Изменить» was pressed on its row.
        if (value?.InStock is { } quantity && StockQuantity.Trim().Length == 0)
            StockQuantity = quantity.ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(SelectedInStockText));
        OnPropertyChanged(nameof(IsSelectedInStock));
    }
    partial void OnStockQuantityChanged(string value) { IsStockQuantityInvalid = false; StockValidation = ""; }
    partial void OnEditingStockChanged(SpareStockRow? value)
    {
        OnPropertyChanged(nameof(IsEditingStock));
        OnPropertyChanged(nameof(IsAddingStock));
        OnPropertyChanged(nameof(EditingStockLabel));
        OnPropertyChanged(nameof(StockActionText));
        OnPropertyChanged(nameof(StockFormTitle));
        OnPropertyChanged(nameof(StockHint));
        OnPropertyChanged(nameof(HasStockHint));
    }

    private void Refresh()
    {
        var selected = SelectedStockPart;
        Parts.Clear();
        foreach (var part in store.Catalog.Where(part => SpareSearch.Matches(PartsSearch, part.Name, part.Article, part.Manufacturer))
                     .OrderBy(part => part.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(part => part.Article))
            Parts.Add(part);
        var manufacturers = store.Catalog.Select(part => part.Manufacturer.Trim()).Where(name => name.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (!manufacturers.SequenceEqual(Manufacturers))
        {
            Manufacturers.Clear();
            foreach (var name in manufacturers) Manufacturers.Add(name);
        }
        StockRows.Clear();
        foreach (var row in store.Stock
                     .Select(item => store.FindPart(item.PartId) is { } part ? new SpareStockRow(part.Id, part.Name, part.Article, store.Available(part.Id)) : null)
                     .Where(row => row is not null && SpareSearch.Matches(StockSearch, row.Name, row.Article))
                     .OrderBy(row => row!.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(row => row!.Article))
            StockRows.Add(row!);
        StockChoices.Clear();
        // The whole catalogue is offered; parts already in stock carry their quantity and are shown in green.
        // The chosen part stays in the list even when the search no longer matches it.
        foreach (var part in store.Catalog
                     .Where(part => part.Id == selected?.Id || SpareSearch.Matches(StockChoiceSearch, part.Name, part.Article))
                     .OrderBy(part => part.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(part => part.Article))
            StockChoices.Add(new SpareStockChoice(part,
                store.Stock.Any(item => item.PartId == part.Id) ? store.Available(part.Id) : null));
        SelectedStockPart = StockChoices.FirstOrDefault(part => part.Id == selected?.Id) ??
            (StockChoices.Count == 1 && StockChoiceSearch.Trim().Length > 0 ? StockChoices[0] : null);
        OnPropertyChanged(nameof(HasParts));
        OnPropertyChanged(nameof(PartsSummary));
        OnPropertyChanged(nameof(HasStockRows));
        OnPropertyChanged(nameof(StockSummary));
        OnPropertyChanged(nameof(StockHint));
        OnPropertyChanged(nameof(HasStockHint));
        OnPropertyChanged(nameof(StockDate));
        OnPropertyChanged(nameof(IsAccountingEnabled));
        OnPropertyChanged(nameof(AccountingText));
    }

    [RelayCommand]
    private void OpenPartForm()
    {
        CancelPartEdit();
        IsPartFormOpen = true;
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
            if (EditingPart is { } part) store.UpdatePart(part.Id, PartName, PartArticle, PartManufacturer);
            else store.AddPart(PartName, PartArticle, PartManufacturer);
            CancelPartEdit();
        }
        catch (Exception ex) { PartValidation = ex.Message; }
    }

    [RelayCommand]
    private void CancelPartEdit()
    {
        EditingPart = null; PartName = ""; PartArticle = ""; PartManufacturer = ""; PartValidation = ""; IsPartNameInvalid = false;
        IsPartFormOpen = false;
    }

    public void BeginEditPart(SparePart part)
    {
        EditingPart = part; PartName = part.Name; PartArticle = part.Article; PartManufacturer = part.Manufacturer;
        PartValidation = ""; IsPartNameInvalid = false;
        IsPartFormOpen = true;
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
    private void OpenStockForm()
    {
        CancelStockEdit();
        IsStockFormOpen = true;
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
        IsStockFormOpen = false;
        StockChoiceSearch = "";
    }

    public void BeginEditStock(SpareStockRow row)
    {
        EditingStock = row;
        StockQuantity = row.Quantity.ToString(CultureInfo.InvariantCulture);
        StockValidation = "";
        IsStockPartInvalid = IsStockQuantityInvalid = false;
        IsStockFormOpen = true;
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

    /// <summary>Closes an open entry form without saving; returns whether one was open (Android Back).</summary>
    public bool CloseForms()
    {
        var open = IsPartFormOpen || IsStockFormOpen;
        if (IsPartFormOpen) CancelPartEdit();
        if (IsStockFormOpen) CancelStockEdit();
        return open;
    }
}
