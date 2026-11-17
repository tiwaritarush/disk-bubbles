# Architecture

## App Cleaner

App Cleaner reads desktop app registrations and the current user’s Store catalog. It measures readable install folders with the same Windows directory reader that Disk Scan uses. Sizes reflect the files Windows returned, and a partial measurement is shown as a lower bound. Uninstall review names each action before opening its registered desktop uninstaller, removing a Store package, or handing off to Windows Settings.

After a desktop uninstall, the current session can identify its remaining install folder. Disk Bubbles checks for other registrations that use the folder before allowing it through the existing Recycle Bin review.

`src/DiskBubbles` contains the native Windows app. `tests/Verification.cs` drives it with disposable fixtures. `Build.ps1` invokes the .NET SDK compiler and embeds the XAML, manifest, and icon into one executable.

## From drive to map

`DiskScanner` assigns directory work to several workers. `NativeDirectoryReader` reads 64 KiB batches of Windows directory records. The scanner adds up subfolders after traversal and orders the root and large lists on the worker. Small lists are sorted on first use. `BubbleMap` draws up to twelve circles per page. Area represents size relative to the largest item on that page, with a minimum to keep small and zero-byte entries visible. The packer checks where each circle can sit against one or two neighbors. During navigation, WPF moves two retained map layers after layout, using bitmap caches when hardware acceleration is available.

Scans observe a live filesystem. Entries can change while a directory is being read, so the map is a view of what the scanner observed during that run. Reparse points discovered during enumeration and unreadable entries are counted as skipped.

## From review to Recycle Bin

Marks belong to a scan and carry the item's original folder path. Review collapses nested marks under a marked parent to avoid moving or counting them twice. It checks each proposed move against the selected path and file identity. Folders also get a fingerprint of descendant identities and timestamps. The app rechecks these before asking Windows to recycle the verified item through `ITransferSource::RecycleItem`. It updates the affected folders when the Shell confirms a move and prompts for a rescan when the result is uncertain.

The Windows transfer uses a pathname. A file can still change between the last identity check and the Shell operation. The verification fixture exercises replacement before that boundary and simulates a confirmed transfer to check the resulting map state. [Windows RecycleItem](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-itransfersource-recycleitem)
