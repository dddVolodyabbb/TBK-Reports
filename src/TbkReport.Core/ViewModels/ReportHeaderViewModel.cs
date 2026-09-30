using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TbkReport.Services;

namespace TbkReport.ViewModels;

public partial class ReportHeaderViewModel : ViewModelBase
{
    public const string DefaultCustomer = "АО «СТНГ»";
    [ObservableProperty] private string customer = DefaultCustomer;
    [ObservableProperty] private string project = "";
    [ObservableProperty] private string site = "";
    [ObservableProperty] private string complex = "";
    [ObservableProperty] private string pipe = "";
    private string collectorNumberText = "";
    public string CollectorNumberText
    {
        get => collectorNumberText;
        set
        {
            value ??= "";
            if (value.Length > 3 || System.Linq.Enumerable.Any(value, character => character < '0' || character > '9'))
            {
                OnPropertyChanged();
                return;
            }
            if (SetProperty(ref collectorNumberText, value)) OnPropertyChanged(nameof(CollectorNumber));
        }
    }
    public int? CollectorNumber => int.TryParse(CollectorNumberText, out var number) ? number : null;
    [ObservableProperty] private string firstShiftTechnician1 = "";
    [ObservableProperty] private string firstShiftTechnician2 = "";
    [ObservableProperty] private string secondShiftTechnician1 = "";
    [ObservableProperty] private string secondShiftTechnician2 = "";
    [ObservableProperty] private string? firstShiftSignature;
    [ObservableProperty] private string? secondShiftSignature;
    [ObservableProperty] private bool isFirstShiftSignatureEditorVisible;
    [ObservableProperty] private bool isSecondShiftSignatureEditorVisible;
    private string? firstShiftSignatureBeforeEdit;
    private string? secondShiftSignatureBeforeEdit;

    public bool HasFirstShiftTechnician1 => !string.IsNullOrWhiteSpace(FirstShiftTechnician1);
    public bool HasSecondShiftTechnician1 => !string.IsNullOrWhiteSpace(SecondShiftTechnician1);
    public bool HasFirstShiftSignature => !string.IsNullOrWhiteSpace(FirstShiftSignature);
    public bool HasSecondShiftSignature => !string.IsNullOrWhiteSpace(SecondShiftSignature);
    public string FirstShiftSignatureActionText => HasFirstShiftSignature ? "Редактировать подпись" : "Добавить подпись";
    public string SecondShiftSignatureActionText => HasSecondShiftSignature ? "Редактировать подпись" : "Добавить подпись";

    partial void OnFirstShiftTechnician1Changed(string value)
    {
        OnPropertyChanged(nameof(HasFirstShiftTechnician1));
        if (!string.IsNullOrWhiteSpace(value)) return;
        FirstShiftSignature = null;
        firstShiftSignatureBeforeEdit = null;
        IsFirstShiftSignatureEditorVisible = false;
    }

    partial void OnSecondShiftTechnician1Changed(string value)
    {
        OnPropertyChanged(nameof(HasSecondShiftTechnician1));
        if (!string.IsNullOrWhiteSpace(value)) return;
        SecondShiftSignature = null;
        secondShiftSignatureBeforeEdit = null;
        IsSecondShiftSignatureEditorVisible = false;
    }

    partial void OnFirstShiftSignatureChanged(string? value)
    {
        OnPropertyChanged(nameof(HasFirstShiftSignature));
        OnPropertyChanged(nameof(FirstShiftSignatureActionText));
    }

    partial void OnSecondShiftSignatureChanged(string? value)
    {
        OnPropertyChanged(nameof(HasSecondShiftSignature));
        OnPropertyChanged(nameof(SecondShiftSignatureActionText));
    }

    [RelayCommand]
    private void ShowFirstShiftSignatureEditor()
    {
        if (IsFirstShiftSignatureEditorVisible) return;
        firstShiftSignatureBeforeEdit = FirstShiftSignature;
        IsFirstShiftSignatureEditorVisible = true;
    }

    [RelayCommand]
    private void ShowSecondShiftSignatureEditor()
    {
        if (IsSecondShiftSignatureEditorVisible) return;
        secondShiftSignatureBeforeEdit = SecondShiftSignature;
        IsSecondShiftSignatureEditorVisible = true;
    }

    [RelayCommand]
    private void CloseFirstShiftSignatureEditor()
    {
        firstShiftSignatureBeforeEdit = null;
        IsFirstShiftSignatureEditorVisible = false;
    }

    [RelayCommand]
    private void CloseSecondShiftSignatureEditor()
    {
        secondShiftSignatureBeforeEdit = null;
        IsSecondShiftSignatureEditorVisible = false;
    }

    [RelayCommand]
    private void CancelFirstShiftSignatureEdit()
    {
        FirstShiftSignature = firstShiftSignatureBeforeEdit;
        firstShiftSignatureBeforeEdit = null;
        IsFirstShiftSignatureEditorVisible = false;
    }

    [RelayCommand]
    private void CancelSecondShiftSignatureEdit()
    {
        SecondShiftSignature = secondShiftSignatureBeforeEdit;
        secondShiftSignatureBeforeEdit = null;
        IsSecondShiftSignatureEditorVisible = false;
    }

    [RelayCommand]
    private void ClearFirstShiftSignature() => FirstShiftSignature = null;

    [RelayCommand]
    private void ClearSecondShiftSignature() => SecondShiftSignature = null;

    public ReportHeaderData ToData() => new(Customer.Trim(), Project.Trim(), Site.Trim(), Complex.Trim(), Pipe.Trim(),
        FirstShiftTechnician1.Trim(), FirstShiftTechnician2.Trim(), SecondShiftTechnician1.Trim(), SecondShiftTechnician2.Trim(),
        CollectorNumber, FirstShiftSignature, SecondShiftSignature);

    public void Load(ReportHeaderData? data)
    {
        Customer = data?.Customer ?? DefaultCustomer;
        Project = data?.Project ?? "";
        Site = data?.Site ?? "";
        Complex = data?.Complex ?? "";
        Pipe = data?.Pipe ?? "";
        CollectorNumberText = data?.CollectorNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        FirstShiftTechnician1 = data?.FirstShiftTechnician1 ?? "";
        FirstShiftTechnician2 = data?.FirstShiftTechnician2 ?? "";
        SecondShiftTechnician1 = data?.SecondShiftTechnician1 ?? "";
        SecondShiftTechnician2 = data?.SecondShiftTechnician2 ?? "";
        FirstShiftSignature = string.IsNullOrWhiteSpace(FirstShiftTechnician1) ? null : data?.FirstShiftSignature;
        SecondShiftSignature = string.IsNullOrWhiteSpace(SecondShiftTechnician1) ? null : data?.SecondShiftSignature;
        IsFirstShiftSignatureEditorVisible = false;
        IsSecondShiftSignatureEditorVisible = false;
        firstShiftSignatureBeforeEdit = null;
        secondShiftSignatureBeforeEdit = null;
    }
}
