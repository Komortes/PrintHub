using PrintHub.Infrastructure.Backends;

namespace PrintHub.Api.Tests;

public sealed class WindowsPrintBackendTests
{
    [Fact]
    public void ResolvePdfToPrinterPath_PrefersBundledExecutable()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"printhub-win-backend-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var bundledExecutablePath = Path.Combine(tempDirectory, "PDFtoPrinter.exe");
            File.WriteAllText(bundledExecutablePath, string.Empty);

            var backend = new WindowsPrintBackend(tempDirectory);

            var resolvedPath = backend.ResolvePdfToPrinterPath();

            Assert.Equal(bundledExecutablePath, resolvedPath);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreatePdfToPrinterStartInfo_UsesSilentModeAndPrinterArguments()
    {
        var toolPath = Path.Combine("tools", "PDFtoPrinter.exe");
        var documentPath = Path.Combine("docs", "label.pdf");

        var startInfo = WindowsPrintBackend.CreatePdfToPrinterStartInfo(
            toolPath,
            documentPath,
            "TSC210_PRINTER-POINT1_label_dopravci",
            copies: 2);

        Assert.Equal(toolPath, startInfo.FileName);
        Assert.Equal(Path.GetDirectoryName(toolPath), startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.Contains(documentPath, startInfo.ArgumentList);
        Assert.Contains("TSC210_PRINTER-POINT1_label_dopravci", startInfo.ArgumentList);
        Assert.Contains("copies=2", startInfo.ArgumentList);
        Assert.Contains("/s", startInfo.ArgumentList);
    }
}
