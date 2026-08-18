using System.Text;
using GalaxyCommand.Content.Validator;

namespace GalaxyCommand.Content.Tests;

public sealed class ContentValidatorApplicationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"galaxy-command-validator-{Guid.NewGuid():N}");

    [Fact]
    public void ValidatorReportsRequestedInspectionData()
    {
        string package = WriteManifest(
            """{"format":"galaxy-command-content-package","schemaVersion":1,"packageId":"core","dependencies":[],"documents":[]}""");
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = ContentValidatorApplication.Run(
            ["--package", package, "--workers", "2", "--show-package-order", "--show-keys", "--show-fingerprints"],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains("package\tcore", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("catalog-fingerprint\t", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("package-fingerprint\tcore\t", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void ValidatorReportsStableDiagnosticCodesWithoutExceptionText()
    {
        string package = WriteManifest(
            """{"format":"wrong","schemaVersion":1,"packageId":"core","dependencies":[],"documents":[]}""");
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = ContentValidatorApplication.Run(["--package", package], output, error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("GC-CONTENT-0009", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatorRejectsMissingPackageOptionAsUsageError()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = ContentValidatorApplication.Run([], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("--package", error.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WriteManifest(string manifest)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "package.json"), manifest, new UTF8Encoding(false));
        return _root;
    }
}
