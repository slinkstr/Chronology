using Chronology.ViewModels;
using Xunit;

namespace Chronology.Tests;

public class MarkSpecialDayViewModelTests
{
    private static MarkSpecialDayViewModel CreateViewModel(Func<string, string?>? validate = null)
    {
        return new MarkSpecialDayViewModel(validate ?? (static _ => null), new DateOnly(2026, 9, 4));
    }

    [Fact]
    public void InitialState_FirstPresetPrefilled_Confirmable()
    {
        var vm = CreateViewModel();
        Assert.Equal("*HOLIDAY", vm.TypeName);
        Assert.True(vm.CanConfirm);
    }

    [Fact]
    public void Date_Null_NotConfirmable()
    {
        var vm = CreateViewModel();
        vm.TypeName = "*HOLIDAY";
        vm.Date = null;

        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void TypePresets_ContainsSpecialDayNames()
    {
        var vm = CreateViewModel();
        Assert.Equal(["*HOLIDAY", "*VACATION", "*SICK"], vm.TypePresets);
    }

    [Fact]
    public void ValidInputs_Confirmable_PreviewShowsParsedDuration()
    {
        var vm = CreateViewModel();
        vm.TypeName = "*HOLIDAY";

        Assert.True(vm.CanConfirm);
        Assert.Equal("= 8:00", vm.ParsedDurationDisplay);
        Assert.Equal("", vm.Error);
    }

    [Fact]
    public void TimeExpression_BareNumber_PreviewShowsMinutesInterpretation()
    {
        var vm = CreateViewModel();
        vm.TypeName = "*SICK";
        vm.TimeExpression = "90";

        Assert.Equal("= 1:30", vm.ParsedDurationDisplay);
        Assert.True(vm.CanConfirm);
    }

    [Fact]
    public void TimeExpression_Invalid_SetsError_NotConfirmable()
    {
        var vm = CreateViewModel();
        vm.TypeName = "*SICK";
        vm.TimeExpression = "abc";

        Assert.Equal("Invalid time expression", vm.Error);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void TimeExpression_Zero_SetsError_NotConfirmable()
    {
        var vm = CreateViewModel();
        vm.TypeName = "*SICK";
        vm.TimeExpression = "0";

        Assert.Equal("Duration must be greater than zero.", vm.Error);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void TypeName_Invalid_SetsError_NotConfirmable()
    {
        var vm = CreateViewModel(s => s.Contains('|') ? "Activity cannot contain '|'." : null);
        vm.TypeName = "a|b";

        Assert.Equal("Activity cannot contain '|'.", vm.Error);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void TimeExpression_NegativeResult_SetsZeroError_NotConfirmable()
    {
        var vm = CreateViewModel();
        vm.TypeName = "*SICK";
        vm.TimeExpression = "1:00 - 2:00";

        Assert.Equal("Duration must be greater than zero.", vm.Error);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void TimeExpression_Overflow_SetsError_NotConfirmable()
    {
        var vm = CreateViewModel();
        vm.TypeName = "*SICK";
        vm.TimeExpression = "999999999:00";

        Assert.Equal("Invalid time expression", vm.Error);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void TypeName_SurroundingWhitespace_ValidatesTrimmedValue()
    {
        var vm = CreateViewModel(s => s == s.Trim() ? null : "No whitespace");
        vm.TypeName = " *SICK ";

        Assert.True(vm.CanConfirm);
        Assert.Equal("", vm.Error);
    }

    [Fact]
    public void TypeName_Empty_NoError_ButNotConfirmable()
    {
        var vm = CreateViewModel();
        vm.TypeName = "";

        Assert.Equal("", vm.Error);
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void BuildResult_ReturnsParsedValues()
    {
        var vm = CreateViewModel();
        vm.TypeName = " *VACATION ";
        vm.TimeExpression = "4:30";

        var result = vm.BuildResult();

        Assert.Equal(new DateOnly(2026, 9, 4), result.Date);
        Assert.Equal("*VACATION", result.Activity);
        Assert.Equal(TimeSpan.FromMinutes(270), result.Duration);
    }
}
