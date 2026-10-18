using Lancir.Core.Matching;

namespace Lancir.Tests.Matching;

public class NameSimilarityTests
{
    [Fact]
    public void Score_ExactMatch_ReturnsOne()
    {
        var score = NameSimilarity.Score("Hollow Knight", "hollow knight");

        Assert.Equal(1.0, score);
    }

    [Fact]
    public void Score_CompletelyDifferentNames_ReturnsLowScore()
    {
        var score = NameSimilarity.Score("Hollow Knight", "Farming Simulator 22");

        Assert.True(score < 0.3, $"Expected low score, got {score}");
    }

    [Fact]
    public void Score_PartialTokenOverlap_ReturnsHigherThanZero()
    {
        var score = NameSimilarity.Score("The Witcher 3 Wild Hunt", "The Witcher 3: Wild Hunt - GOTY Edition");

        Assert.True(score > 0.5, $"Expected reasonably high score, got {score}");
    }

    [Fact]
    public void Score_EmptyInput_ReturnsZero()
    {
        var score = NameSimilarity.Score("", "Portal 2");

        Assert.Equal(0.0, score);
    }
}
