# Contributing

Bug reports are most helpful when they include the Disk Bubbles version, Windows version, drive type, and steps to reproduce. Screenshots help with map or layout bugs. Redact account names, complete paths, and other private details from screenshots and logs.

Use [GitHub Issues](https://github.com/tiwaritarush/disk-bubbles/issues) for bugs and feature ideas. Send security and data-safety reports through the [private reporting instructions](SECURITY.md).

## Build and verify a change

```powershell
.\Build.ps1
.\Verify.ps1
```

Use the disposable fixture for changes to scanning or Recycle Bin review. Give performance comparisons a fixed fixture or a clear description of the drive, cache state, and entry count. Keep commits focused. A subject such as `fix: preserve the file list after cancelling a scan` or `feat: add a keyboard shortcut for the drive picker` makes the history easy to follow.

In a pull request, describe the problem, the change, and how you checked it. Include before-and-after screenshots for a visible UI change.

## Package a release

Run `./scripts/Package.ps1 -Ref v10.0.1` after tagging the release commit. The script builds and verifies a fresh source archive from that commit and writes the executable, source ZIP, and SHA-256 checksums to `artifacts/release/`.
