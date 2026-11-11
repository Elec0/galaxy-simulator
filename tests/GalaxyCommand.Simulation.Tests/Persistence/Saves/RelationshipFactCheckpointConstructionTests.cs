using System.Reflection;

namespace GalaxyCommand.Simulation.Tests;

public sealed class RelationshipFactCheckpointConstructionTests
{
    [Fact]
    public void RevocationFactRestoresOnlyItsRetainedIdentityAndReason()
    {
        ConstructorInfo? constructor = typeof(RelationshipGrantRevokedFact).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(RelationshipGrantId), typeof(RelationshipPolicyChangeReason)],
            modifiers: null);

        Assert.NotNull(constructor);
        var fact = Assert.IsType<RelationshipGrantRevokedFact>(constructor.Invoke(
            [new RelationshipGrantId(17), RelationshipPolicyChangeReason.Explicit]));
        Assert.Equal(new RelationshipGrantId(17), fact.Id);
        Assert.Equal(RelationshipPolicyChangeReason.Explicit, fact.Reason);
        TargetInvocationException error = Assert.Throws<TargetInvocationException>(() =>
            constructor.Invoke([default(RelationshipGrantId), RelationshipPolicyChangeReason.Explicit]));
        Assert.IsType<ArgumentOutOfRangeException>(error.InnerException);
    }
}
