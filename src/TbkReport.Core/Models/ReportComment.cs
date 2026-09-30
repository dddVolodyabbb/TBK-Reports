using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TbkReport.Models;

public partial class ReportComment : ObservableObject
{
    public ReportComment(TimeSpan time, string text, TimeSpan? endTime = null)
    {
        Time = time;
        Text = text;
        EndTime = endTime;
    }

    [ObservableProperty] private TimeSpan time;
    [ObservableProperty] private TimeSpan? endTime;
    [ObservableProperty] private string text;

    public string TimeText => EndTime is { } end
        ? Time.ToString(@"hh\:mm") + "–" + end.ToString(@"hh\:mm")
        : Time.ToString(@"hh\:mm");
    partial void OnTimeChanged(TimeSpan value) => OnPropertyChanged(nameof(TimeText));
    partial void OnEndTimeChanged(TimeSpan? value) => OnPropertyChanged(nameof(TimeText));
}
