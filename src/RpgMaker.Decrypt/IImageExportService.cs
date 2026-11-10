namespace RpgMaker.Decrypt;

/// <summary>
/// Deferred export API. See specs/export-deferred.md (status: deferred).
/// No implementation in the current release.
/// </summary>
public interface IImageExportService
{
    Task ExportPngAsync(string sourcePath, string destinationPath, CancellationToken ct);
}
