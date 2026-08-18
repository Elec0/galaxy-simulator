using GalaxyCommand.Content;

namespace GalaxyCommand.Content.Tests;

public sealed class ContentIdentityTests
{
    [Theory]
    [InlineData("galaxy-command.core", "ship-design", "freighter-mk1")]
    [InlineData("core2", "material2", "iron2")]
    public void QualifiedKeyAcceptsConstrainedLowercaseAscii(
        string packageId,
        string contentKind,
        string localId)
    {
        QualifiedContentKey key = QualifiedContentKey.Create(packageId, contentKind, localId);

        Assert.Equal(packageId, key.PackageId.Value);
        Assert.Equal(contentKind, key.ContentKind.Value);
        Assert.Equal(localId, key.LocalId.Value);
        Assert.Equal($"{packageId}/{contentKind}/{localId}", key.ToString());
    }

    [Theory]
    [InlineData("Galaxy.Command", "material", "iron")]
    [InlineData("galaxy_command", "material", "iron")]
    [InlineData("galaxy.command", "ship/design", "freighter")]
    [InlineData("galaxy.command", "ship-design", "freighter.mk1")]
    public void QualifiedKeyRejectsValuesOutsideTheApprovedGrammar(
        string packageId,
        string contentKind,
        string localId)
    {
        Assert.Throws<ArgumentException>(
            () => QualifiedContentKey.Create(packageId, contentKind, localId));
    }
}
