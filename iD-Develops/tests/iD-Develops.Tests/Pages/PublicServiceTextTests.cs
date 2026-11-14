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

    [Theory]
    [InlineData("Learning is difficult. That's not strange. We can help.", "That's not strange.", "Learning is difficult.", "That's not strange. We can help.")]
    [InlineData("Leren kost tijd. Dat is niet gek: we helpen je.", "Dat is niet gek:", "Leren kost tijd.", "Dat is niet gek: we helpen je.")]
    [InlineData("We practise. We practise together.", "We practise", "We practise. We practise together.", "")]
    [InlineData("We practise together.", "Missing boundary", "We practise together.", "")]
    [InlineData("First idea. Next idea. Next step.", "Next", "First idea.", "Next idea. Next step.")]
    public void EditorialSplitPreservesTheBoundaryAndAllOriginalText(string original, string marker, string opening, string remainder)
    {
        var text = PublicServiceText.SplitBefore(original, marker);
        Assert.Equal(opening, text.Opening);
        Assert.Equal(remainder, text.Remainder);
        Assert.Equal(Normalize(original), Normalize($"{text.Opening} {text.Remainder}"));
    }

    [Fact]
    public void EditorialSplitRejectsAnEmptyBoundary()
    {
        Assert.Throws<ArgumentException>(() => PublicServiceText.SplitBefore("Existing text.", " "));
    }

    [Theory]
    [InlineData("One idea. Another sentence about the same idea.", 1)]
    [InlineData("We meet at 18.00. Bring your questions.", 1)]
    [InlineData("A wrapped line\ncontinues the same thought.", 1)]
    [InlineData("One idea.\n\nA new topic.", 2)]
    [InlineData("Eén onderwerp.\r\n\r\nEen nieuw onderwerp.", 2)]
    [InlineData("First idea.\n\n\n\nSecond idea.", 2)]
    [InlineData("  \n\n  ", 0)]
    [InlineData("", 0)]
    public void ParagraphsRespectAuthoredBreaksAndPreserveTheCopy(string original, int count)
    {
        var paragraphs = PublicServiceText.Paragraphs(original);
        Assert.Equal(count, paragraphs.Count);
        Assert.Equal(Normalize(original), Normalize(string.Join(" ", paragraphs)));
    }

    [Fact]
    public void ParagraphsDoNotInterpretMarkup()
    {
        Assert.Equal("<script>alert('text')</script>", Assert.Single(PublicServiceText.Paragraphs("<script>alert('text')</script>")));
    }

    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
