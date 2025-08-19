# CmdKit

A lightweight Windows command / snippet / credential quick launcher built with .NET 8 WinForms.

## Features
- Fast search with debounce filtering
- Custom categories (free-form Type field)
- Dark / Light / Blossom (pink) themes
- Tray mode + global hotkey (Ctrl+Q) to toggle visibility
- Add / Edit / Delete / Import / Export JSON
- Auto-close after copy (optional)
- Sensitive value protection (DPAPI per-user) with regex-based detection
- Shift-hover to temporarily reveal secret in tooltip (otherwise masked)
- Eye toggle in editor to show/hide secret contents
- Single-file self?contained publish option (.NET runtime included)

## Sensitive Data Protection
Values whose name, type, or description match configurable regex patterns (default: `password`, `token`, `secret`, `pwd`, `api[-_]?key`) are automatically encrypted when saved.
Encryption uses Windows DPAPI (CurrentUser); protected values can only be decrypted under the same user profile on the same machine.
Stored format: `__ENC__<base64>`.

Limitations:
- Moving the JSON file to another machine / user will make protected entries unreadable.
- Not designed for multi-user shared network storage.
- For cross-machine portability a master password–based key derivation would be needed (not implemented).

## Files & Storage
User data is stored in `%AppData%/CmdKit`:
- `commands.json` : Encrypted / plain entries (auto-encrypted as needed)
- `settings.json`

## Building
```bash
# Restore & build
dotnet build

# Publish single-file (self-contained) for distribution
 dotnet publish CmdKit/CmdKit.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish/win-x64
```
Resulting executable: `publish/win-x64/CmdKit.exe`.

## Import / Export Format
`commands.json` is a list of entries:
```json
[
  {
    "Id": "<guid>",
    "Name": "List S3 Buckets",
    "Value": "aws s3 ls",
    "Description": "AWS CLI list buckets",
    "Kind": "AWS",
    "CreatedUtc": "2024-07-20T09:13:00Z",
    "UpdatedUtc": "2024-07-20T09:13:00Z"
  }
]
```
Encrypted secrets have `Value` starting with `__ENC__`.

## Hotkey
- Global: `Ctrl+Q` shows/hides the window
- Enter / Double-click / Ctrl+C: copy selected value
- Shift + hover (list): reveal secret temporarily (tooltip)

## Themes
Configured via Settings (?). Blossom applies soft gradient.

## Adding Secrets
- Mark "Encrypt" in editor OR rely on automatic regex detection.
- On save the value is encrypted and masked in tooltip.

## Extending Sensitive Detection
Add regex patterns to `SensitivePatterns` in settings.json. Example:
```json
"SensitivePatterns": [
  "password",
  "token",
  "secret",
  "pwd",
  "api[-_]?key",
  "client[-_]?secret"
]
```

## Planned Improvements (Ideas)
- Master password derived key (cross-device portability)
- Category color tags
- Sync / cloud backend
- Quick inline edit, favorites, pinning
- Multi-select export / delete

## License
Internal use. Add license text here if needed.
