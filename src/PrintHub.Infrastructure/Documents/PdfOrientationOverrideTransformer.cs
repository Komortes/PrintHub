using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PrintHub.Contracts.PrintJobs;

namespace PrintHub.Infrastructure.Documents;

internal static class PdfOrientationOverrideTransformer
{
    public static Task ApplyAsync(
        string documentPath,
        PrintDocumentOrientationOverride orientationOverride,
        CancellationToken cancellationToken = default)
    {
        if (orientationOverride == PrintDocumentOrientationOverride.Auto)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() => Apply(documentPath, orientationOverride), cancellationToken);
    }

    public static void Apply(
        string documentPath,
        PrintDocumentOrientationOverride orientationOverride)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        if (!Enum.IsDefined(orientationOverride))
        {
            throw new ArgumentOutOfRangeException(
                nameof(orientationOverride),
                "Unsupported orientation override value.");
        }

        if (orientationOverride == PrintDocumentOrientationOverride.Auto)
        {
            return;
        }

        var outputPath = Path.Combine(
            Path.GetDirectoryName(documentPath) ?? AppContext.BaseDirectory,
            $"{Path.GetFileNameWithoutExtension(documentPath)}-oriented{Path.GetExtension(documentPath)}");

        try
        {
            using var document = PdfReader.Open(documentPath, PdfDocumentOpenMode.Modify);

            foreach (var page in document.Pages.Cast<PdfPage>())
            {
                ApplyOrientation(page, orientationOverride);
            }

            document.Save(outputPath);
            File.Move(outputPath, documentPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            throw;
        }
    }

    private static void ApplyOrientation(
        PdfPage page,
        PrintDocumentOrientationOverride orientationOverride)
    {
        var currentRotation = NormalizeRotation(page.Rotate);
        var isLandscape = IsLandscape(page, currentRotation);
        var targetLandscape = orientationOverride == PrintDocumentOrientationOverride.Landscape;

        if (isLandscape == targetLandscape)
        {
            return;
        }

        page.Rotate = NormalizeRotation(currentRotation + 90);
    }

    private static bool IsLandscape(PdfPage page, int normalizedRotation)
    {
        var width = page.Width.Point;
        var height = page.Height.Point;

        if (normalizedRotation is 90 or 270)
        {
            (width, height) = (height, width);
        }

        return width > height;
    }

    private static int NormalizeRotation(int rotation)
    {
        var normalizedRotation = rotation % 360;

        if (normalizedRotation < 0)
        {
            normalizedRotation += 360;
        }

        return normalizedRotation;
    }
}
