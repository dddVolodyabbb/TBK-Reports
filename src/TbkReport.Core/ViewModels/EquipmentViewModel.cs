using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TbkReport.Services;

namespace TbkReport.ViewModels;

public partial class EquipmentViewModel : ViewModelBase
{
    public ObservableCollection<EquipmentData> Items { get; } = new();
    [ObservableProperty] private string draftName = "";
    [ObservableProperty] private string draftInWork = "0";
    [ObservableProperty] private string draftInReserve = "0";
    /// <summary>Serial numbers, free text such as «10033, 10032».</summary>
    [ObservableProperty] private string draftNumbers = "";
    /// <summary>Chosen from <see cref="Manufacturers"/>; empty means not specified.</summary>
    [ObservableProperty] private string? draftManufacturer;
    /// <summary>
    /// Manufacturers of the spare parts catalogue (filled by the report editor); the first entry, «Не указан», clears
    /// the choice. A manufacturer of an existing entry that is no longer in the catalogue stays offered while it is edited.
    /// </summary>
    public ObservableCollection<string> Manufacturers { get; } = new() { NoManufacturer };
    public const string NoManufacturer = "Не указан";
    [ObservableProperty] private string validation = "";
    [ObservableProperty] private EquipmentData? editingItem;
    // Fields marked by the last add/save attempt; editing a field removes its mark until the next attempt.
    [ObservableProperty] private bool isNameInvalid;
    [ObservableProperty] private bool isInWorkInvalid;
    [ObservableProperty] private bool isInReserveInvalid;
    partial void OnDraftNameChanged(string value) => IsNameInvalid = false;
    partial void OnDraftInWorkChanged(string value) => IsInWorkInvalid = false;
    partial void OnDraftInReserveChanged(string value) => IsInReserveInvalid = false;
    public bool IsEditing => EditingItem is not null;
    public string ActionText => IsEditing ? "Сохранить изменения" : "Добавить оборудование";
    public bool HasDraft => IsEditing || !string.IsNullOrWhiteSpace(DraftName) || DraftInWork != "0" || DraftInReserve != "0" ||
        !string.IsNullOrWhiteSpace(DraftNumbers) || ChosenManufacturer.Length > 0;
    private string ChosenManufacturer => DraftManufacturer is null || DraftManufacturer == NoManufacturer ? "" : DraftManufacturer.Trim();

    /// <summary>Replaces the offered manufacturers, keeping the current choice.</summary>
    public void SetManufacturers(IEnumerable<string> names)
    {
        var chosen = DraftManufacturer;
        var list = new[] { NoManufacturer }.Concat(names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(chosen) && !list.Contains(chosen, StringComparer.CurrentCultureIgnoreCase)) list.Add(chosen);
        if (list.SequenceEqual(Manufacturers)) return;
        Manufacturers.Clear();
        foreach (var name in list) Manufacturers.Add(name);
        DraftManufacturer = chosen is null ? null : list.FirstOrDefault(name => string.Equals(name, chosen, StringComparison.CurrentCultureIgnoreCase));
    }

    private void OfferManufacturer(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Manufacturers.Contains(name, StringComparer.CurrentCultureIgnoreCase)) return;
        Manufacturers.Add(name);
    }
    /// <summary>Raised after the user adds, changes or removes equipment (not on <see cref="Load"/>): the list is saved then.</summary>
    public event Action? ItemsChanged;
    partial void OnEditingItemChanged(EquipmentData? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(ActionText));
    }

    [RelayCommand]
    private void AddOrSave()
    {
        // All fields are checked at once, so every wrong field is marked in one go.
        IsNameInvalid = string.IsNullOrWhiteSpace(DraftName);
        IsInWorkInvalid = !int.TryParse(DraftInWork, NumberStyles.None, CultureInfo.InvariantCulture, out var work);
        IsInReserveInvalid = !int.TryParse(DraftInReserve, NumberStyles.None, CultureInfo.InvariantCulture, out var reserve);
        if (IsNameInvalid || IsInWorkInvalid || IsInReserveInvalid)
        {
            Validation = IsNameInvalid
                ? IsInWorkInvalid || IsInReserveInvalid
                    ? "Введите название оборудования. Количество должно быть целым неотрицательным числом."
                    : "Введите название оборудования."
                : "Количество должно быть целым неотрицательным числом.";
            return;
        }
        var entry = new EquipmentData(DraftName.Trim(), work, reserve, DraftNumbers.Trim(), ChosenManufacturer);
        var index = EditingItem is null ? -1 : IndexOf(EditingItem);
        if (index >= 0) Items[index] = entry;
        else Items.Add(entry);
        CancelEdit();
        ItemsChanged?.Invoke();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingItem = null; DraftName = ""; DraftInWork = "0"; DraftInReserve = "0"; Validation = "";
        DraftNumbers = ""; DraftManufacturer = null;
        IsNameInvalid = IsInWorkInvalid = IsInReserveInvalid = false;
    }

    private int IndexOf(EquipmentData item)
    {
        for (var i = 0; i < Items.Count; i++) if (ReferenceEquals(Items[i], item)) return i;
        return -1;
    }
    public void BeginEdit(EquipmentData item)
    {
        if (IndexOf(item) < 0) return;
        EditingItem = item; DraftName = item.Name;
        DraftInWork = item.InWork.ToString(CultureInfo.InvariantCulture);
        DraftInReserve = item.InReserve.ToString(CultureInfo.InvariantCulture);
        DraftNumbers = item.Numbers ?? "";
        var manufacturer = (item.Manufacturer ?? "").Trim();
        OfferManufacturer(manufacturer);
        DraftManufacturer = manufacturer.Length == 0 ? null
            : Manufacturers.First(name => string.Equals(name, manufacturer, StringComparison.CurrentCultureIgnoreCase));
        Validation = "";
        IsNameInvalid = IsInWorkInvalid = IsInReserveInvalid = false;
    }
    public void Remove(EquipmentData item)
    {
        if (ReferenceEquals(EditingItem, item)) CancelEdit();
        var index = IndexOf(item);
        if (index < 0) return;
        Items.RemoveAt(index);
        ItemsChanged?.Invoke();
    }
    public EquipmentData[] ToData() => Items.ToArray();
    public void Load(IReadOnlyList<EquipmentData>? entries)
    {
        CancelEdit(); Items.Clear();
        foreach (var entry in entries ?? Array.Empty<EquipmentData>()) Items.Add(entry);
    }
}
