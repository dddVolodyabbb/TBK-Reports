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
    [ObservableProperty] private string validation = "";
    [ObservableProperty] private EquipmentData? editingItem;
    public bool IsEditing => EditingItem is not null;
    public string ActionText => IsEditing ? "Сохранить изменения" : "Добавить оборудование";
    public bool HasDraft => IsEditing || !string.IsNullOrWhiteSpace(DraftName) || DraftInWork != "0" || DraftInReserve != "0";
    partial void OnEditingItemChanged(EquipmentData? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(ActionText));
    }

    [RelayCommand]
    private void AddOrSave()
    {
        if (string.IsNullOrWhiteSpace(DraftName)) { Validation = "Введите название оборудования."; return; }
        if (!int.TryParse(DraftInWork, NumberStyles.None, CultureInfo.InvariantCulture, out var work) ||
            !int.TryParse(DraftInReserve, NumberStyles.None, CultureInfo.InvariantCulture, out var reserve))
        { Validation = "Количество должно быть целым неотрицательным числом."; return; }
        var entry = new EquipmentData(DraftName.Trim(), work, reserve);
        var index = EditingItem is null ? -1 : IndexOf(EditingItem);
        if (index >= 0) Items[index] = entry;
        else Items.Add(entry);
        CancelEdit();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingItem = null; DraftName = ""; DraftInWork = "0"; DraftInReserve = "0"; Validation = "";
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
        Validation = "";
    }
    public void Remove(EquipmentData item)
    {
        if (ReferenceEquals(EditingItem, item)) CancelEdit();
        var index = IndexOf(item);
        if (index >= 0) Items.RemoveAt(index);
    }
    public EquipmentData[] ToData() => Items.ToArray();
    public void Load(IReadOnlyList<EquipmentData>? entries)
    {
        CancelEdit(); Items.Clear();
        foreach (var entry in entries ?? Array.Empty<EquipmentData>()) Items.Add(entry);
    }
}
