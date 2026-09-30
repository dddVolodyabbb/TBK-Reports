using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using TbkReport.Models;
using TbkReport.Services;
namespace TbkReport.ViewModels;
public partial class ShiftViewModel : ViewModelBase
{
    private const string StartCommentText = "Старт СМР";
    private const string StopCommentText = "Стоп СМР";
    [ObservableProperty] private string firstJoint = "";
    [ObservableProperty] private string lastJoint = "";
    [ObservableProperty] private string weldedCountText = "";
    [ObservableProperty] private bool isWeldedCountInvalid;
    private bool updatingCountNumbers;
    private bool loadingReport;
    public bool IsCountMode { get; private set; }
    public bool IsNumberMode => !IsCountMode;
    public string FirstJointDisplay => string.IsNullOrEmpty(FirstJoint) ? "—" : FirstJoint;
    public string LastJointDisplay => string.IsNullOrEmpty(LastJoint) ? "—" : LastJoint;
    public string CountValidation => !IsCountMode || WeldedCountText.Length == 0 ? "" :
        !int.TryParse(WeldedCountText, out var count) || count <= 0
            ? "Введите положительное целое количество стыков."
            : int.TryParse(FirstJoint, out var first) && (long)first + count - 1 > int.MaxValue
                ? "Последний номер стыка слишком большой." : "";
    public bool HasCountValidation => CountValidation.Length > 0;
    [ObservableProperty] private TimeSpan? startTime;
    [ObservableProperty] private TimeSpan? stopTime;
    [ObservableProperty] private string repairs = "";
    [ObservableProperty] private TimeSpan? newCommentTime;
    [ObservableProperty] private TimeSpan? newCommentEndTime;
    [ObservableProperty] private bool isCommentTimeRange;
    [ObservableProperty] private string commentValidation = "";
    [ObservableProperty] private string newCommentText = "";
    [ObservableProperty] private ReportComment? editingComment;
    [ObservableProperty] private bool isStartTimeInvalid;
    [ObservableProperty] private bool isFirstJointInvalid;
    [ObservableProperty] private bool isLastJointInvalid;
    [ObservableProperty] private bool isStopTimeInvalid;
    public ObservableCollection<ReportComment> Comments { get; } = new();
    public bool HasComments => Comments.Count > 0;
    private bool automatic;
    public bool FirstManuallyEdited { get; private set; }
    public int Welded => int.TryParse(FirstJoint, out var first) && int.TryParse(LastJoint, out var last) && first > 0 && last >= first ? last - first + 1 : 0;
    public string JointValidation => (FirstJoint.Length > 0 && (!int.TryParse(FirstJoint, out var first) || first <= 0)) || (LastJoint.Length > 0 && (!int.TryParse(LastJoint, out var last) || last <= 0)) ? "Введите положительные целые номера стыков." : int.TryParse(FirstJoint, out var a) && int.TryParse(LastJoint, out var b) && b < a ? "Последний стык не может быть меньше первого." : "";
    public string RepairsValidation => Repairs.Length > 0 && (!int.TryParse(Repairs, out var count) || count < 0) ? "Введите неотрицательное целое число ремонтов." : "";
    public bool HasJointValidation => JointValidation.Length > 0;
    public bool HasRepairsValidation => RepairsValidation.Length > 0;
    public string Validation => CountValidation.Length > 0 ? CountValidation : JointValidation.Length > 0 ? JointValidation : RepairsValidation;
    public int? RepairCount => int.TryParse(Repairs, out var count) && count >= 0 ? count : null;
    public bool IsEditingComment => EditingComment is not null;
    public bool IsCommentEndTimeVisible => IsCommentTimeRange;
    public string CommentActionText => IsEditingComment ? "Сохранить изменения" : "Добавить комментарий";
    public bool HasCommentValidation => CommentValidation.Length > 0;
    public ShiftViewModel() => Comments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasComments));
    partial void OnFirstJointChanged(string value)
    {
        if (!automatic) FirstManuallyEdited = true;
        if (!string.IsNullOrWhiteSpace(value)) IsFirstJointInvalid = false;
        RecalculateLastJoint();
        OnPropertyChanged(nameof(FirstJointDisplay));
        Refresh();
    }
    partial void OnLastJointChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) IsLastJointInvalid = false;
        OnPropertyChanged(nameof(LastJointDisplay));
        Refresh();
    }
    partial void OnStartTimeChanged(TimeSpan? value)
    {
        if (value is not null) IsStartTimeInvalid = false;
        SynchronizeTimeComment(value, StartCommentText);
    }
    partial void OnStopTimeChanged(TimeSpan? value)
    {
        if (value is not null) IsStopTimeInvalid = false;
        SynchronizeTimeComment(value, StopCommentText);
    }
    private void Refresh()
    {
        OnPropertyChanged(nameof(Welded));
        OnPropertyChanged(nameof(JointValidation));
        OnPropertyChanged(nameof(RepairsValidation));
        OnPropertyChanged(nameof(CountValidation));
        OnPropertyChanged(nameof(HasJointValidation));
        OnPropertyChanged(nameof(HasRepairsValidation));
        OnPropertyChanged(nameof(HasCountValidation));
        OnPropertyChanged(nameof(Validation));
    }
    partial void OnWeldedCountTextChanged(string value)
    {
        IsWeldedCountInvalid = false;
        RecalculateLastJoint();
        Refresh();
    }

    public void SetCountMode(bool enabled)
    {
        if (IsCountMode == enabled) return;
        var count = Welded;
        IsCountMode = enabled;
        if (enabled) WeldedCountText = count > 0 ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        IsWeldedCountInvalid = false;
        OnPropertyChanged(nameof(IsCountMode));
        OnPropertyChanged(nameof(IsNumberMode));
        Refresh();
    }

    private void RecalculateLastJoint()
    {
        if (!IsCountMode || updatingCountNumbers || loadingReport) return;
        updatingCountNumbers = true;
        try
        {
            LastJoint = int.TryParse(FirstJoint, out var first) && first > 0 &&
                int.TryParse(WeldedCountText, out var count) && count > 0 && (long)first + count - 1 <= int.MaxValue
                ? ((long)first + count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        }
        finally { updatingCountNumbers = false; }
    }
    public void SetAutomaticFirst(string value)
    {
        if ((!IsCountMode && FirstManuallyEdited) || string.IsNullOrEmpty(value)) return;
        automatic = true;
        FirstJoint = value;
        automatic = false;
    }

    public void ChangeCollectorStart(string value)
    {
        var count = Welded;
        var hasInput = StartTime is not null || FirstJoint.Length > 0 || LastJoint.Length > 0 || WeldedCountText.Length > 0;
        FirstManuallyEdited = false;
        if (!hasInput) return;
        SetAutomaticFirst(value);
        if (!IsCountMode)
            LastJoint = count > 0 && int.TryParse(value, out var first) && (long)first + count - 1 <= int.MaxValue
                ? ((long)first + count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
    }

    public bool HighlightMissingRequiredFields(bool highlightMissingStart = false)
    {
        var started = StartTime is not null;
        IsStartTimeInvalid = highlightMissingStart && !started;
        IsFirstJointInvalid = started && string.IsNullOrWhiteSpace(FirstJoint);
        IsLastJointInvalid = started && string.IsNullOrWhiteSpace(LastJoint);
        IsStopTimeInvalid = started && StopTime is null;
        IsWeldedCountInvalid = IsCountMode && started && (WeldedCountText.Length == 0 || CountValidation.Length > 0);
        return IsStartTimeInvalid || IsFirstJointInvalid || IsLastJointInvalid || IsStopTimeInvalid || IsWeldedCountInvalid;
    }

    partial void OnRepairsChanged(string value) => Refresh();
    partial void OnIsCommentTimeRangeChanged(bool value)
    {
        CommentValidation = "";
        if (!value) NewCommentEndTime = null;
        else NewCommentEndTime ??= NewCommentTime ?? DateTime.Now.TimeOfDay;
        OnPropertyChanged(nameof(IsCommentEndTimeVisible));
    }
    partial void OnCommentValidationChanged(string value) => OnPropertyChanged(nameof(HasCommentValidation));
    partial void OnEditingCommentChanged(ReportComment? value)
    {
        OnPropertyChanged(nameof(IsEditingComment));
        OnPropertyChanged(nameof(CommentActionText));
    }

    [RelayCommand]
    private void AddComment()
    {
        var text = NewCommentText.Trim();
        if (text.Length == 0) return;
        var time = NewCommentTime;
        var endTime = IsCommentEndTimeVisible ? NewCommentEndTime : null;
        if (time is null)
        {
            CommentValidation = "Укажите время комментария.";
            return;
        }
        if (IsCommentEndTimeVisible && endTime is null)
        {
            CommentValidation = "Укажите окончание диапазона времени.";
            return;
        }
        if (EditingComment is { } comment)
        {
            comment.Time = time.Value;
            comment.EndTime = endTime;
            comment.Text = text;
            EditingComment = null;
            SortCommentsByTime();
        }
        else
        {
            Comments.Add(new ReportComment(time.Value, text, endTime));
            SortCommentsByTime();
        }
        ResetCommentDraft();
    }

    public void BeginEditComment(ReportComment comment)
    {
        if (!Comments.Contains(comment)) return;
        EditingComment = comment;
        NewCommentTime = comment.Time;
        NewCommentEndTime = comment.EndTime;
        IsCommentTimeRange = comment.EndTime is not null;
        NewCommentText = comment.Text;
    }

    public void EnsureNewCommentTime()
    {
        if (NewCommentTime is not null) return;
        var now = DateTime.Now;
        NewCommentTime = new TimeSpan(now.Hour, now.Minute, 0);
    }

    [RelayCommand]
    private void CancelCommentEdit()
    {
        EditingComment = null;
        ResetCommentDraft();
    }

    private void ResetCommentDraft()
    {
        NewCommentText = "";
        IsCommentTimeRange = false;
        NewCommentTime = null;
        NewCommentEndTime = null;
        CommentValidation = "";
    }

    public void RemoveComment(ReportComment comment)
    {
        if (EditingComment == comment) CancelCommentEdit();
        Comments.Remove(comment);
    }

    private void SortCommentsByTime()
    {
        var ordered = Comments.OrderBy(comment => comment.Time).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var currentIndex = Comments.IndexOf(ordered[index]);
            if (currentIndex != index) Comments.Move(currentIndex, index);
        }
    }

    private void SynchronizeTimeComment(TimeSpan? time, string text)
    {
        var matches = Comments.Where(comment => comment.Text == text).ToArray();
        if (time is null)
        {
            foreach (var match in matches) Comments.Remove(match);
            return;
        }

        var automaticComment = matches.FirstOrDefault();
        if (automaticComment is null)
            Comments.Add(new ReportComment(time.Value, text));
        else
        {
            automaticComment.Time = time.Value;
            automaticComment.EndTime = null;
            foreach (var duplicate in matches.Skip(1)) Comments.Remove(duplicate);
        }
        SortCommentsByTime();
    }

    public void LoadReport(ShiftReportData data)
    {
        loadingReport = true;
        try
        {
        CancelCommentEdit();
        automatic = true;
        FirstJoint = data.FirstJoint;
        LastJoint = data.LastJoint;
        automatic = false;
        FirstManuallyEdited = FirstJoint.Length > 0;
        StartTime = data.StartTime;
        StopTime = data.StopTime;
        Repairs = data.Repairs?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        Comments.Clear();
        foreach (var comment in data.Comments.OrderBy(comment => comment.Time))
            Comments.Add(new ReportComment(comment.Time, comment.Text, comment.EndTime));
        SynchronizeTimeComment(StartTime, StartCommentText);
        SynchronizeTimeComment(StopTime, StopCommentText);
        WeldedCountText = Welded > 0 ? Welded.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
        }
        finally { loadingReport = false; }
    }

    public ShiftDraftData ToDraftData() => new(
        new ShiftReportData(FirstJoint, LastJoint, StartTime, StopTime, RepairCount, Comments.ToArray()),
        Repairs, NewCommentTime, NewCommentEndTime, NewCommentText, IsCommentTimeRange,
        EditingComment is null ? -1 : Comments.IndexOf(EditingComment),
        IsCountMode ? WeldedCountText : null, FirstManuallyEdited);

    public void LoadDraft(ShiftDraftData data)
    {
        LoadReport(data.Report);
        FirstManuallyEdited = data.FirstManuallyEdited ?? FirstManuallyEdited;
        if (IsCountMode && data.WeldedCountText is not null) WeldedCountText = data.WeldedCountText;
        Repairs = data.RepairsText;
        if (data.EditingCommentIndex >= 0 && data.EditingCommentIndex < Comments.Count)
            BeginEditComment(Comments[data.EditingCommentIndex]);
        else EditingComment = null;
        NewCommentTime = data.NewCommentTime;
        NewCommentEndTime = data.NewCommentEndTime;
        IsCommentTimeRange = data.IsCommentTimeRange;
        NewCommentText = data.NewCommentText;
    }
}
