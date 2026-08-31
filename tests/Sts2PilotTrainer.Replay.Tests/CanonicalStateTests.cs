namespace Sts2PilotTrainer.Replay.Tests;

public class CanonicalStateTests
{
    [Fact]
    public void OrdersFieldsSoTwoBuildOrdersProduceTheSameDigest()
    {
        var forwards = CanonicalState.Build().Add("b", 2).Add("a", 1).ToState();
        var backwards = CanonicalState.Build().Add("a", 1).Add("b", 2).ToState();

        Assert.Equal(forwards.Digest(), backwards.Digest());
    }

    [Fact]
    public void ProducesADifferentDigestForDifferentState()
    {
        var left = CanonicalState.Build().Add("hp", 64).ToState();
        var right = CanonicalState.Build().Add("hp", 63).ToState();

        Assert.NotEqual(left.Digest(), right.Digest());
    }

    [Fact]
    public void TreatsSequenceOrderAsState()
    {
        // Hand and draw order are outcomes of the shuffle stream, not presentation.
        var left = CanonicalState.Build().AddSequence("hand", ["STRIKE", "DEFEND"]).ToState();
        var right = CanonicalState.Build().AddSequence("hand", ["DEFEND", "STRIKE"]).ToState();

        Assert.NotEqual(left.Digest(), right.Digest());
    }

    [Fact]
    public void RefusesToProjectTheSameFieldTwice()
    {
        // Silently keeping the last write would let a projection bug look like state.
        var builder = CanonicalState.Build().Add("hp", 1);

        var thrown = Assert.Throws<InvalidOperationException>(() => builder.Add("hp", 2));
        Assert.Contains("projected twice", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsEveryDifferenceBetweenTwoStates()
    {
        var left = CanonicalState.Build().Add("hp", 64).Add("gold", 99).ToState();
        var right = CanonicalState.Build().Add("hp", 60).Add("block", 5).ToState();

        var differences = CanonicalState.Diff(left, right);

        Assert.Collection(
            differences,
            d => Assert.Equal(new CanonicalState.StateDifference("block", "<absent>", "5"), d),
            d => Assert.Equal(new CanonicalState.StateDifference("gold", "99", "<absent>"), d),
            d => Assert.Equal(new CanonicalState.StateDifference("hp", "64", "60"), d));
    }

    [Fact]
    public void FindsNoDifferenceBetweenIdenticalStates()
    {
        var left = CanonicalState.Build().Add("hp", 64).ToState();
        var right = CanonicalState.Build().Add("hp", 64).ToState();

        Assert.Empty(CanonicalState.Diff(left, right));
    }

    [Fact]
    public void DocumentsWhatItExcludesAndWhy()
    {
        // The exclusions are load-bearing: they are what makes a digest mismatch mean
        // a real divergence rather than a different afternoon. An empty or unexplained
        // list would mean nobody had made the decision on purpose.
        Assert.NotEmpty(CanonicalState.ExcludedByDesign);
        Assert.All(CanonicalState.ExcludedByDesign, excluded =>
        {
            Assert.False(string.IsNullOrWhiteSpace(excluded.Category));
            Assert.False(string.IsNullOrWhiteSpace(excluded.What));
            Assert.False(string.IsNullOrWhiteSpace(excluded.Why));
        });

        Assert.Contains(CanonicalState.ExcludedByDesign, e => e.Category == "wall_clock");
        Assert.Contains(CanonicalState.ExcludedByDesign, e => e.Category == "object_identity");
        Assert.Contains(CanonicalState.ExcludedByDesign, e => e.Category == "filesystem_paths");
    }

    [Fact]
    public void RendersTheExactTextThatGetsHashed()
    {
        var state = CanonicalState.Build().Add("a", 1).Add("b", "two").ToState();

        Assert.Equal("a=1\nb=two\n", state.Render());
    }
}
