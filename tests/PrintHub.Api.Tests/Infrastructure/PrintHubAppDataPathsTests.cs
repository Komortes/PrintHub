using PrintHub.Infrastructure.Paths;

namespace PrintHub.Api.Tests.Infrastructure;

public sealed class PrintHubAppDataPathsTests
{
    [Fact]
    public void ResolveDataPath_UsesAppDataRootForRelativePaths()
    {
        var tempRootPath = Path.Combine(Path.GetTempPath(), $"printhub-paths-{Guid.NewGuid():N}");

        try
        {
            var paths = new PrintHubAppDataPaths(tempRootPath);

            var resolvedPath = paths.ResolveDataPath("data/documents", "data/default");

            Assert.Equal(
                Path.Combine(tempRootPath, "data", "documents"),
                resolvedPath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRootPath))
                {
                    Directory.Delete(tempRootPath, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public void ResolveDataPath_PreservesAbsolutePaths()
    {
        var tempRootPath = Path.Combine(Path.GetTempPath(), $"printhub-paths-{Guid.NewGuid():N}");

        try
        {
            var paths = new PrintHubAppDataPaths(tempRootPath);
            var absolutePath = Path.Combine(tempRootPath, "external", "docs");

            var resolvedPath = paths.ResolveDataPath(absolutePath, "data/default");

            Assert.Equal(absolutePath, resolvedPath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRootPath))
                {
                    Directory.Delete(tempRootPath, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
