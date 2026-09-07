using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Trainer.Tests;

public sealed class ModelIdNameTests
{
    [Theory]
    [InlineData("ACT.UNDERDOCKS", "Underdocks")]
    [InlineData("CHARACTER.IRONCLAD", "Ironclad")]
    [InlineData("ENCOUNTER.SLUDGE_SPINNER_WEAK", "Sludge Spinner Weak")]
    [InlineData("Underdocks", "Underdocks")]
    public void ModelIdsReadAsNames(string modelId, string expected) =>
        Assert.Equal(expected, ModelIdNames.Display(modelId));
}
