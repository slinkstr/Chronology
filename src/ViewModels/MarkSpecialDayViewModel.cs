using Chronology.Services;
using Chronology.Services.TimeLogStores;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chronology.ViewModels;

public partial class MarkSpecialDayViewModel : ViewModelBase
{
    private static readonly string[] Presets = ["*HOLIDAY", "*VACATION", "*SICK"];

    private readonly Func<string, string?> _validateName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    private DateTime? _date;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(Error))]
    [NotifyPropertyChangedFor(nameof(ParsedDurationDisplay))]
    private string _timeExpression = "8:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(Error))]
    private string _typeName = Presets[0];

    public bool      CanConfirm            => Date is not null && !string.IsNullOrWhiteSpace(TypeName) && _validateName(TypeName.Trim()) is null && ParsedDuration is not null;
    public string    Error                 => ComputeError();
    public TimeSpan? ParsedDuration        => TimeExpressionParser.TryParse(TimeExpression, out var parsed) && parsed > TimeSpan.Zero ? parsed : null;
    public string    ParsedDurationDisplay => ParsedDuration is { } parsed ? $"= {DurationText.Format(parsed, DurationFormat.Hhmm)}" : "";
    public IReadOnlyList<string> TypePresets => Presets;

    public MarkSpecialDayViewModel(Func<string, string?> validateName, DateOnly date)
    {
        _validateName = validateName;
        _date = date.ToDateTime(TimeOnly.MinValue);
    }

    public MarkSpecialDayResult BuildResult()
    {
        return new MarkSpecialDayResult(DateOnly.FromDateTime(Date!.Value), TypeName.Trim(), ParsedDuration!.Value);
    }

    private string ComputeError()
    {
        if (!string.IsNullOrWhiteSpace(TypeName) && _validateName(TypeName.Trim()) is { } nameError)
        {
            return nameError;
        }
        if (string.IsNullOrWhiteSpace(TimeExpression))
        {
            return "";
        }
        if (!TimeExpressionParser.TryParse(TimeExpression, out var raw, allowNegative: true))
        {
            return "Invalid time expression";
        }
        return raw <= TimeSpan.Zero ? "Duration must be greater than zero." : "";
    }
}

public record MarkSpecialDayResult(DateOnly Date, string Activity, TimeSpan Duration);
