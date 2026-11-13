using System.Text.RegularExpressions;
using iD_Develops.Models;

namespace iD_Develops.Tests.Pages;

public sealed class PublicServiceTextTests
{
    [Theory]
    [InlineData("We know how it feels. You can make mistakes here. We learn together.")]
    [InlineData("Je mag fouten maken! Samen oefenen we. Dat maakt leren leuker.")]
    [InlineData("Lessons finish at 18.00. Then we practise together. Bring your questions.")]
    [InlineData("‘Is this it?’ We ask questions. You find your own answers.")]
    [InlineData("Learning together 🌍. Everyone is welcome. Bring your own perspective.")]
    [InlineData("One sentence without a boundary")]
    [InlineData("")]
    public void LayoutSplitsPreserveEveryWordAndPunctuation(string text)
    {
        foreach (var result in new[] { PublicServiceText.Lead(text), PublicServiceText.Balanced(text) })
            Assert.Equal(Normalize(text), Normalize($"{result.Opening} {result.Remainder}"));
    }

    [Fact]
    public void OpeningSplitKeepsNumbersInsideTheSentence()
    {
        var text = PublicServiceText.Lead("We meet at 18.00 every day. You choose your own pace.");
        Assert.Equal("We meet at 18.00 every day.", text.Opening);
        Assert.Equal("You choose your own pace.", text.Remainder);
    }

    [Fact]
    public void BalancedSplitUsesACompleteSentenceBoundary()
    {
        var text = PublicServiceText.Balanced("One short sentence. Another short sentence. A final short sentence.");
        Assert.EndsWith(".", text.Opening);
        Assert.Contains(text.Remainder, new[] { "Another short sentence. A final short sentence.", "A final short sentence." });
    }

    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
