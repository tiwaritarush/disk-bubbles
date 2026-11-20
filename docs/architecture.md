# Architecture

## App Cleaner

App Cleaner reads desktop app registrations and the current user’s Store catalog. It measures readable install folders with the same Windows directory reader that Disk Scan uses. Sizes reflect the files Windows returned, and a partial measurement is shown as a lower bound.

Uninstall review names each action before launching a verified MSI or vendor removal command or removing a supported Store package for the current user. Unsupported commands produce an explanation inside App Cleaner. A confirmed removal updates the visible collection directly. Manual scans refresh the full inventory.

Deep Uninstall also looks for matching app data and caches. Candidate paths come from registered install locations, exact app and publisher names, or a Store package family. Cleanup rejects protected locations, filesystem links, driver files, and folders used by another app or service. A fresh check runs before each reviewed Recycle Bin move. Leftover discoveries belong to the current app session.

The verification fixture covers removal-command validation, both uninstall review flows, rejected cleanup candidates, themed busy states, and updates to the visible app list. Live Desktop and Store uninstalls still need manual validation.

`src/DiskBubbles` contains the native Windows app. `tests/Verification.cs` drives it with disposable fixtures. `Build.ps1` invokes the .NET SDK compiler and embeds the XAML, manifest, and icon into one executable.

## From drive to map

`DiskScanner` assigns directory work to several workers. `NativeDirectoryReader` reads 64 KiB batches of Windows directory records. The scanner adds up subfolders after traversal and orders the root and large lists on the worker. Small lists are sorted on first use. `BubbleMap` draws up to twelve circles per page. Area represents size relative to the largest item on that page, with a minimum to keep small and zero-byte entries visible. The packer checks where each circle can sit against one or two neighbors. During navigation, WPF moves two retained map layers after layout, using bitmap caches when hardware acceleration is available.

Scans observe a live filesystem. Entries can change while a directory is being read, so the map is a view of what the scanner observed during that run. Reparse points discovered during enumeration and unreadable entries are counted as skipped.

The default worker limit depends on the volume's [Windows seek-penalty descriptor](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ns-winioctl-device_seek_penalty_descriptor). A successful query reporting no seek penalty permits up to twice the logical processor count, capped at 32 workers. Seeking disks and failed or unsupported queries retain the previous limit of eight workers or the logical processor count, whichever is smaller. The query opens a volume handle with zero requested read/write access and uses [IOCTL_STORAGE_QUERY_PROPERTY](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_storage_query_property). It does not read raw disk contents. [Performance measurements](performance.md) document the tuning.

## From review to Recycle Bin

Marks belong to a scan and carry the item's original folder path. Review collapses nested marks under a marked parent and checks each item's path, type, size and file identity without re-scanning folder contents. `RecycleBin` re-checks identity just before the move, then hands every item to one [IFileOperation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperation) with Recycle Bin flags and a progress sink. Each item's outcome comes from Windows' `PostDeleteItem` report, or from the filesystem when Windows reports nothing. See [Selection and deletion](selection-and-deletion.md).

The Windows transfer uses a pathname, so a file can still change between the last identity check and the Shell operation; the window is a few milliseconds and the destination is the Recycle Bin.
