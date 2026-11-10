# Export (deferred)

**status: deferred**

Do **not** implement in the current release. Spec exists only to lock future scope and allow an interface stub.

## Intended behavior (future)

- Export a single encrypted image to a decrypted `.png` path chosen by the user.
- Optional later: batch export of a folder.

## Interface stub (current release)

```csharp
public interface IImageExportService
{
    Task ExportPngAsync(string sourcePath, string destinationPath, CancellationToken ct);
}
```

- Define the interface in `RpgMaker.Decrypt`.
- No implementation class.
- No UI entry points in the viewer.
- No CLI export flags.

## Future acceptance (when activated)

1. Export writes a valid PNG that opens in standard image apps.
2. Overwrite policy is explicit (confirm or fail).
3. Failures surface a clear error without crashing the viewer.
