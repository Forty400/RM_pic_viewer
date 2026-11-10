# Shell Thumbnail Provider

## Goal

When Windows Explorer is in Large Icons / Extra Large Icons (and other thumbnail modes), `.rpgmvp` and `.png_` files show decrypted image thumbnails.

## COM contract

- Implement `IThumbnailProvider` plus path-based init: `IInitializeWithItem` and `IInitializeWithFile`.
- Path init is required because `encryptionKey` is resolved by walking up from the file path to `System.json` (stream-only init cannot find the key and must fail cleanly).
- CLSID (fixed for this product): `{A7B3C1D2-4E5F-6789-ABCD-EF0123456789}`
- Register under each extension:
  - `HKCR\.rpgmvp\ShellEx\{e357fccd-a995-4576-b01f-234630154e96}` → CLSID
  - `HKCR\.png_\ShellEx\{e357fccd-a995-4576-b01f-234630154e96}` → CLSID
- COM activation via .NET `EnableComHosting` (`*.comhost.dll` registered with `regsvr32`).

## Behavior

- Resolve path → decrypt via shared library using [decrypt.md](decrypt.md).
- Produce an `HBITMAP` at the requested size; Explorer scales as needed.
- On any failure: return a failure HRESULT; Explorer shows the default icon. **Must not crash `explorer.exe`.**

## Dev registration

- `tools/register-shell.ps1` supports register and `-Unregister`.
- After register/unregister: restart Explorer or clear thumbnail cache for verification.

## Acceptance

1. Register provider.
2. Open a game `img` folder in Explorer, switch to Large Icons.
3. Encrypted images show recognizable thumbnails (not blank generic icons).
4. Unregister restores default icons.
