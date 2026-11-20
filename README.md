# Disk Bubbles

Disk Bubbles shows where your Windows storage went. Scan a drive or folder and get a map of glass bubbles whose area is the space they use. The biggest folders are easy to spot, and a click takes you straight into one.

[Download for Windows](https://github.com/tiwaritarush/disk-bubbles/releases/latest) · [Changelog](CHANGELOG.md) · [Build from source](#build-from-source) · [Report an issue](https://github.com/tiwaritarush/disk-bubbles/issues)

Windows 10 and 11 · Portable executable · C# and WPF · MIT

![Disk Bubbles in dark mode. A frosted pane with a list of folders sits beside a map of circles sized by storage use.](docs/screenshots/dark.png)

Windows can tell you that a drive is nearly full. Figuring out which project, download, or backup is responsible often means opening one folder after another. Disk Bubbles puts the sizes and hierarchy in the same view so you can go straight to the folders worth investigating.

## What you can do

- **Scan a drive or folder.** See progress as Windows enumerates the files. You can cancel a scan and return to your previous results.
- **Explore by size.** Bubble area is true to size, and folders show their biggest contents inside them. Click a folder in the list or on the map to open it. Hover to see its size and share of the current folder. When a folder holds more than fits legibly, a **+N more** bubble and the **Next page** button show the rest. Back and Forward remember your scroll position and map page.
- **Mark items as you browse.** Check files or folders anywhere in the scan, then choose **Review & delete** in the footer. Every item ends with a clear result: moved to the Recycle Bin, permanently deleted after the Windows prompt for items too large for the Bin, or kept with the reason.
- **Uninstall apps.** App Cleaner lists desktop and Store apps with their icons and measured install sizes. Choose **Uninstall** or **Deep Uninstall** from the app details pane. Confirmed removals leave the list immediately, and the current filter stays in place.
- **Review leftovers.** After a supported uninstall, review remaining install folders. Deep Uninstall also checks matching app data and caches. Choose whether to move verified folders to the Recycle Bin.
- **Switch between light and dark.** Both themes are liquid glass over soft coloured light. Folder bubbles glow green in the dark theme and pink in the light one, and files carry a subtle tint for their kind. The app remembers your choice and follows the Windows animation setting.

![Disk Bubbles in the cream and ink theme with frosted glass bubbles.](docs/screenshots/light.png)

![App Cleaner with installed app icons, measured sizes, and Uninstall and Deep Uninstall buttons inside the details pane.](docs/screenshots/app-cleaner.png)

Screenshots use a disposable sample library and example app names.

## Run it

Get `DiskBubbles.exe` from the [releases page](https://github.com/tiwaritarush/disk-bubbles/releases) and run it on Windows 10 or 11 with .NET Framework 4.8 or later. It runs with your usual Windows permissions. The portable executable is unsigned, and the release includes a SHA-256 checksum for checking the download.

Start with **Scan C:** or **Choose folder…**. Click a folder name or bubble to open it. The last bubble you hover stays highlighted in the list until you hover another one. Right-click a bubble to open it, mark it, move it straight to the Recycle Bin, or show it in File Explorer.

Checkboxes mark items for a combined review. Protected Windows and app locations cannot be marked, and their checkbox says why. A completed removal shows its actual size, what was recycled or permanently deleted, and anything that stayed with its reason. [Selection and deletion](docs/selection-and-deletion.md) describes the checks.

Press **Enter** on a row to open its folder or reveal its file, **Space** to mark it and **Delete** to review it. Use **Alt+Left**, **Alt+Right**, **Alt+Up** or **Backspace** to navigate, and **Page Up**/**Page Down** to page the map. A new scan starts a fresh set of marks.

The map shows logical file sizes. The space a file takes up on disk can differ, especially for compressed, sparse, or hard-linked files. The capacity meter uses the space Windows reports for your account. Scan results stay in memory, and Disk Bubbles sends no telemetry.

App Cleaner runs verified MSI or vendor uninstallers and supported Store removal for the current Windows account. Cleanup checks folder ownership against installed apps and services, then checks file identities before recycling. Protected driver packages and shared components stay under Windows or vendor control. Apps with unsupported removal commands show an explanation in the app.

## Build from source

Install the .NET SDK on Windows. From the repository root in PowerShell, run the following commands.

```powershell
.\Build.ps1
.\Verify.ps1
```

The build writes `artifacts/DiskBubbles.exe`. Verification runs against files it creates under `artifacts/verification/` and saves screenshots there. It exercises the UI and safeguards and moves disposable fixture files to your Recycle Bin to prove each outcome. Release details and remaining validation gaps are in the [changelog](CHANGELOG.md).

## How it works

Disk Bubbles is written in **C#** with a **WPF and XAML** interface on **.NET Framework 4.8**. The scanner reads Windows directory records in batches and uses several workers to cover a volume. It uses more workers on storage that Windows reports without a seek penalty. The map uses native WPF drawings, which are cached when you go back to a folder. The app has no external package dependencies.

[Scan measurements and the benchmark command](docs/performance.md) explain the current worker limits and their measured effect.

See [the architecture notes](docs/architecture.md) for the scan and Recycle Bin boundaries. For patches and bug reports, see [Contributing](CONTRIBUTING.md). For a potential vulnerability, use the [security reporting instructions](SECURITY.md).

Disk Bubbles is available under the [MIT License](LICENSE).
