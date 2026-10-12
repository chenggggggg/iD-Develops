using iD_Develops.Utilities;

namespace iD_Develops.Tests.Utilities;

public sealed class ExamDurationTests
{
    [Fact]
    public void TryParse_PreservesSecondPrecision()
    {
        var isValid = ExamDuration.TryParse("01:30:45", out var totalSeconds);

        Assert.True(isValid);
        Assert.Equal(5445, totalSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_TreatsBlankAsNoLimit(string? value)
    {
        var isValid = ExamDuration.TryParse(value, out var totalSeconds);

        Assert.True(isValid);
        Assert.Null(totalSeconds);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("01:60:00")]
    [InlineData("90")]
    [InlineData("not-a-duration")]
    public void TryParse_RejectsInvalidDurations(string value)
    {
        Assert.False(ExamDuration.TryParse(value, out _));
    }

    [Fact]
    public void Format_DoesNotWrapHoursAfterOneDay()
    {
        Assert.Equal("25:02:03", ExamDuration.Format((25 * 3600) + (2 * 60) + 3));
    }
}
