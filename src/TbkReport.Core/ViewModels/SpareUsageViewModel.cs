using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TbkReport.Services;

namespace TbkReport.ViewModels;

/// <summary>
/// A part offered in the shift's list. With the stock checked it shows the pieces this shift may still take;
/// without the check (accounting off, or a report before «Остатки на дату») no number is shown.
/// </summary>
public sealed record SpareChoice(string PartId, string Name, string Article, int Available, bool ShowAvailable = true)
{
    public string Label => (string.IsNullOrEmpty(Article) ? Name : $"{Name} · {Article}") +
        (ShowAvailable ? $" (в наличии {Available})" : "");
}

/// <summary>
/// Spare parts (ЗИП) used in one shift, always chosen from «В наличии». With «Учёт ЗИП» on, a line is accepted only
/// when the stock covers it, so the check happens at once, when the line is added to the report. With it off, nothing
/// is checked and no numbers are shown: the usage is only recorded with the report.
/// </summary>
public partial class SpareUsageViewModel : ViewModelBase
{
    public ObservableCollection<SpareUsage> Items { get; } = new();
    public ObservableCollection<SpareChoice> Choices { get; } = new();
    [ObservableProperty] private SpareChoice? selectedChoice;
    /// <summary>Narrows <see cref="Choices"/> by name or article; a single match is selected at once.</summary>
    [ObservableProperty] private string choiceSearch = "";
    /// <summary>The search finds nothing (a part chosen earlier may still be listed).</summary>
    public bool HasNoMatches => hasStock && matchCount == 0;
    private bool hasStock;
    private int matchCount;
    [ObservableProperty] private string draftQuantity = "";
    [ObservableProperty] private string validation = "";
    [ObservableProperty] private SpareUsage? editingItem;
    // Fields marked by the last add/save attempt; editing a field removes its mark until the next attempt.
    [ObservableProperty] private bool isPartInvalid;
    [ObservableProperty] private bool isQuantityInvalid;
    private bool refreshingChoices;

    /// <summary>The stock; without it (an editor with no spare parts accounting) nothing can be added.</summary>
    public SpareStore? Store { get; set; }

    /// <summary>
    /// Pieces of a part taken outside this shift's own lines: the other shift of the same report, minus what
    /// the stock already gave to the saved report that is being edited.
    /// </summary>
    public Func<string, int>? ReservedElsewhere { get; set; }

    /// <summary>Whether this report's usage is checked against the stock (accounting on and the report from the stock date on).</summary>
    public Func<bool>? ChecksStock { get; set; }

    /// <summary>Parts always come from «В наличии»; the accounting only decides whether the stock is reduced and checked.</summary>
    public bool ChoosesFromStock => Store is not null;
    public string Hint => Store?.AccountingEnabled == true
        ? "Что израсходовано за смену. Количество сразу сверяется с наличием, а списывается при сохранении отчёта."
        : "Что израсходовано за смену. Учёт ЗИП выключен: расход записывается в отчёт, но из наличия не вычитается.";
    public string SearchPlaceholder => "Найти в наличии";
    public string ChoicePlaceholder => "Выберите из наличия";

    public bool HasItems => Items.Count > 0;
    public bool HasChoices => Choices.Count > 0;
    /// <summary>The stock list is empty while parts come from it: nothing can be chosen whatever is searched.</summary>
    public bool IsStockEmpty => !hasStock && ChoosesFromStock;
    public string Header => Items.Count > 0 ? $"Расход ЗИП ({Items.Count})" : "Расход ЗИП";
    public bool IsEditing => EditingItem is not null;
    public string ActionText => IsEditing ? "Сохранить изменения" : "Добавить в расход";
    public bool HasDraft => IsEditing || DraftQuantity.Trim().Length > 0;

    public SpareUsageViewModel() => Items.CollectionChanged += (_, _) =>
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Header));
    };

    partial void OnSelectedChoiceChanged(SpareChoice? value)
    {
        if (refreshingChoices) return;
        IsPartInvalid = false;
        Validation = "";
    }
    partial void OnChoiceSearchChanged(string value) => RefreshChoices();
    partial void OnDraftQuantityChanged(string value)
    {
        IsQuantityInvalid = false;
        Validation = "";
    }
    partial void OnEditingItemChanged(SpareUsage? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(ActionText));
        RefreshChoices();
    }

    /// <summary>Pieces this shift may still take, not counting the line being edited.</summary>
    private int Free(string partId) =>
        (Store?.Available(partId) ?? 0) - (ReservedElsewhere?.Invoke(partId) ?? 0) -
        Items.Where(item => !ReferenceEquals(item, EditingItem) && item.PartId == partId).Sum(item => item.Quantity);

    /// <summary>Rebuilds the list of stock positions; called after any change of the stock or of the report's usage.</summary>
    public void RefreshChoices()
    {
        var selectedId = SelectedChoice?.PartId;
        refreshingChoices = true;
        try
        {
            Choices.Clear();
            var all = new List<SpareChoice>();
            var checks = ChecksStock?.Invoke() == true;
            if (Store is { } store)
            {
                var parts = ChoosesFromStock
                    ? store.Stock.Select(stock => store.FindPart(stock.PartId)).Where(part => part is not null).Select(part => part!)
                    : store.Catalog;
                foreach (var part in parts)
                    all.Add(new SpareChoice(part.Id, part.Name, part.Article, checks ? Math.Max(0, Free(part.Id)) : 0, checks));
            }
            // A line being edited stays selectable even when its part has left the list.
            if (EditingItem is { } editing && all.All(choice => choice.PartId != editing.PartId))
                all.Add(new SpareChoice(editing.PartId, editing.Name, editing.Article,
                    checks ? Math.Max(0, Free(editing.PartId)) : 0, checks));
            hasStock = all.Count > 0;
            matchCount = all.Count(choice => SpareSearch.Matches(ChoiceSearch, choice.Name, choice.Article));
            // The chosen part stays in the list even when the search no longer matches it.
            foreach (var choice in all.Where(choice => choice.PartId == selectedId ||
                             SpareSearch.Matches(ChoiceSearch, choice.Name, choice.Article))
                         .OrderBy(choice => choice.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(choice => choice.Article))
                Choices.Add(choice);
            SelectedChoice = Choices.FirstOrDefault(choice => choice.PartId == selectedId) ??
                (Choices.Count == 1 && ChoiceSearch.Trim().Length > 0 ? Choices[0] : null);
        }
        finally { refreshingChoices = false; }
        OnPropertyChanged(nameof(HasChoices));
        OnPropertyChanged(nameof(IsStockEmpty));
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(ChoosesFromStock));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(ChoicePlaceholder));
    }

    [RelayCommand]
    private void AddOrSave()
    {
        var choice = SelectedChoice;
        IsPartInvalid = choice is null;
        IsQuantityInvalid = !int.TryParse(DraftQuantity.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0;
        if (IsPartInvalid || IsQuantityInvalid)
        {
            Validation = IsPartInvalid
                ? IsQuantityInvalid ? "Выберите комплектующую и введите количество больше нуля." : "Выберите комплектующую."
                : "Количество должно быть целым числом больше нуля.";
            return;
        }
        var free = Free(choice!.PartId);
        if (ChecksStock?.Invoke() == true && quantity > free)
        {
            IsQuantityInvalid = true;
            Validation = free <= 0
                ? "Этой комплектующей больше нет в наличии."
                : $"Столько нет в наличии. Можно списать не больше {free} шт.";
            return;
        }
        var index = EditingItem is null ? -1 : IndexOf(EditingItem);
        // The same part entered twice in a shift is kept as one line.
        var same = Items.FirstOrDefault(item => !ReferenceEquals(item, EditingItem) && item.PartId == choice.PartId);
        if (same is not null)
        {
            quantity += same.Quantity;
            if (index >= 0) Items.RemoveAt(index);
            index = IndexOf(same);
        }
        var entry = new SpareUsage(choice.PartId, choice.Name, choice.Article, quantity);
        EditingItem = null;
        if (index >= 0) Items[index] = entry;
        else Items.Add(entry);
        CancelEdit();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingItem = null; SelectedChoice = null; DraftQuantity = ""; Validation = "";
        IsPartInvalid = IsQuantityInvalid = false;
        ChoiceSearch = "";
    }

    private int IndexOf(SpareUsage item)
    {
        for (var i = 0; i < Items.Count; i++) if (ReferenceEquals(Items[i], item)) return i;
        return -1;
    }

    public void BeginEdit(SpareUsage item)
    {
        if (IndexOf(item) < 0) return;
        EditingItem = item;
        SelectedChoice = Choices.FirstOrDefault(choice => choice.PartId == item.PartId);
        DraftQuantity = item.Quantity.ToString(CultureInfo.InvariantCulture);
        Validation = "";
        IsPartInvalid = IsQuantityInvalid = false;
    }

    public void Remove(SpareUsage item)
    {
        if (ReferenceEquals(EditingItem, item)) CancelEdit();
        var index = IndexOf(item);
        if (index >= 0) Items.RemoveAt(index);
    }

    public SpareUsage[] ToData() => Items.ToArray();

    public void Load(IReadOnlyList<SpareUsage>? entries)
    {
        CancelEdit();
        Items.Clear();
        foreach (var entry in entries ?? Array.Empty<SpareUsage>())
            if (entry is { PartId: not null, Quantity: > 0 })
                Items.Add(entry with { Name = entry.Name ?? "", Article = entry.Article ?? "" });
    }
}
