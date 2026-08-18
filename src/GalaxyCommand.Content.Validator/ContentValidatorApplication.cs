using System.Collections.ObjectModel;

namespace GalaxyCommand.Content.Validator;

/// <summary>Hosts the production content pipeline without Godot or session mutation.</summary>
public static class ContentValidatorApplication
{
    /// <summary>
    /// Parses command arguments, validates selected package directories, and
    /// writes deterministic inspection output or stable diagnostics.
    /// </summary>
    public static int Run(
        IReadOnlyList<string> args,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        try
        {
            ValidatorRequest request = Parse(args);
            if (request.ShowHelp)
            {
                standardOutput.WriteLine(HelpText);
                return 0;
            }

            ContentKindRegistry registry = new(request.AllowedKinds.Select(ContentKind.Create));
            ContentValidationResult result = ContentPipeline.Validate(
                request.PackageDirectories,
                new ContentValidationOptions(
                    ContentJsonLimits.ProductionDefaults,
                    registry,
                    request.Workers));
            if (!result.IsSuccess)
            {
                foreach (ContentDiagnostic diagnostic in result.Diagnostics)
                {
                    standardError.WriteLine(
                        $"{ContentDiagnosticCodes.GetCode(diagnostic.Kind)}\t{diagnostic.Source}\t{diagnostic.Path}\t{diagnostic.Message}");
                }

                return 1;
            }

            WriteInspection(result.Content!, request, standardOutput);
            return 0;
        }
        catch (ValidatorUsageException exception)
        {
            standardError.WriteLine($"content_validator_usage: {exception.Message}");
            standardError.WriteLine("Use --help for command syntax.");
            return 2;
        }
        catch (Exception)
        {
            // Headless output must not expose unstable exception text or local stack details.
            standardError.WriteLine("content_validator_failure: unexpected internal failure");
            return 1;
        }
    }

    private static string HelpText =>
        """
        Galaxy Command production content validator

        Usage:
          dotnet run --project src/GalaxyCommand.Content.Validator -- --package DIR [options]

        Options:
          --package DIR             Select a package directory; may be repeated.
          --allow-kind KIND         Register a trusted content kind; may be repeated.
          --workers COUNT           Bound read-only package loading workers; defaults to 1.
          --show-package-order      Print resolved dependency-first package order.
          --show-keys               Print canonical qualified-key inventory.
          --show-fingerprints       Print package and catalog fingerprints.
          --help                    Show this help.
        """;

    /// <summary>
    /// Parses only explicit options so filesystem contents cannot add packages,
    /// trusted kinds, or inspection behavior.
    /// </summary>
    private static ValidatorRequest Parse(IReadOnlyList<string> args)
    {
        List<string> packages = [];
        List<string> kinds = [];
        int workers = 1;
        bool showPackageOrder = false;
        bool showKeys = false;
        bool showFingerprints = false;
        bool showHelp = false;
        for (int index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--package":
                    packages.Add(NextValue(args, ref index, "--package"));
                    break;
                case "--allow-kind":
                    kinds.Add(NextValue(args, ref index, "--allow-kind"));
                    break;
                case "--workers":
                    string value = NextValue(args, ref index, "--workers");
                    if (!int.TryParse(value, out workers) || workers <= 0)
                    {
                        throw new ValidatorUsageException("--workers requires a positive integer.");
                    }

                    break;
                case "--show-package-order":
                    showPackageOrder = true;
                    break;
                case "--show-keys":
                    showKeys = true;
                    break;
                case "--show-fingerprints":
                    showFingerprints = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new ValidatorUsageException($"Unknown option '{args[index]}'.");
            }
        }

        if (!showHelp && packages.Count == 0)
        {
            throw new ValidatorUsageException("At least one --package directory is required.");
        }

        return new ValidatorRequest(
            new ReadOnlyCollection<string>(packages),
            new ReadOnlyCollection<string>(kinds),
            workers,
            showPackageOrder,
            showKeys,
            showFingerprints,
            showHelp);
    }

    private static string NextValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Count)
        {
            throw new ValidatorUsageException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }

    private static void WriteInspection(
        ResolvedContentSet content,
        ValidatorRequest request,
        TextWriter output)
    {
        if (request.ShowPackageOrder)
        {
            foreach (PackageId packageId in content.PackageOrder)
            {
                output.WriteLine($"package\t{packageId.Value}");
            }
        }

        if (request.ShowKeys)
        {
            foreach (QualifiedContentKey key in content.Catalog.Definitions.Keys)
            {
                output.WriteLine($"key\t{key}");
            }
        }

        if (request.ShowFingerprints)
        {
            foreach ((PackageId packageId, string fingerprint) in content.PackageFingerprints)
            {
                output.WriteLine($"package-fingerprint\t{packageId.Value}\t{fingerprint}");
            }

            output.WriteLine($"catalog-fingerprint\t{content.CatalogFingerprint}");
        }
    }

    private sealed record ValidatorRequest(
        IReadOnlyList<string> PackageDirectories,
        IReadOnlyList<string> AllowedKinds,
        int Workers,
        bool ShowPackageOrder,
        bool ShowKeys,
        bool ShowFingerprints,
        bool ShowHelp);

    private sealed class ValidatorUsageException(string message) : Exception(message);
}
