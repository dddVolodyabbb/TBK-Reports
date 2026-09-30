using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TbkReport.ViewModels;

public partial class ProblemItem : ObservableObject
{
    [ObservableProperty] private int number;
    [ObservableProperty] private string text = "";
    public string Label => $"{Number})";
    partial void OnNumberChanged(int value) => OnPropertyChanged(nameof(Label));
}

public partial class ProblemsViewModel : ViewModelBase
{
    public ObservableCollection<ProblemItem> Items { get; } = new();
    [ObservableProperty] private string draftText = "";
    [ObservableProperty] private ProblemItem? editingItem;
    public bool IsEditing => EditingItem is not null;
    public string ActionText => IsEditing ? "Сохранить изменения" : "Добавить проблему";
    public bool HasDraft => IsEditing || !string.IsNullOrWhiteSpace(DraftText);
    partial void OnEditingItemChanged(ProblemItem? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(ActionText));
    }

    [RelayCommand]
    private void AddOrSave()
    {
        var text = DraftText.Trim();
        if (text.Length == 0) return;
        if (EditingItem is { } item) item.Text = text;
        else Items.Add(new ProblemItem { Number = Items.Count + 1, Text = text });
        CancelEdit();
    }

    [RelayCommand]
    private void CancelEdit() { EditingItem = null; DraftText = ""; }

    public void BeginEdit(ProblemItem item)
    {
        if (!Items.Contains(item)) return;
        EditingItem = item;
        DraftText = item.Text;
    }

    public void Remove(ProblemItem item)
    {
        if (EditingItem == item) CancelEdit();
        Items.Remove(item);
        for (var i = 0; i < Items.Count; i++) Items[i].Number = i + 1;
    }

    public string[] ToData() => Items.Select(item => item.Text).ToArray();
    public void Load(IReadOnlyList<string>? texts)
    {
        CancelEdit();
        Items.Clear();
        foreach (var text in texts ?? System.Array.Empty<string>())
            Items.Add(new ProblemItem { Number = Items.Count + 1, Text = text });
    }
}
