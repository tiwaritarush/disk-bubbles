# Architecture

`src/DiskBubbles` contains the native Windows app. `tests/Verification.cs` drives it with disposable fixtures. `Build.ps1` invokes the .NET SDK compiler and embeds the XAML, manifest, and icon into one executable.

## From drive to map

`DiskScanner` assigns directory work to several workers. `NativeDirectoryReader` reads 64 KiB batches of Windows directory records. The scanner adds up subfolders after traversal and orders the root and large lists on the worker. Small lists are sorted on first use. `BubbleMap` draws up to eleven circles, with area proportional to the item size. Remaining entries can be inspected through **Other items** or directly in the file list.

Scans observe a live filesystem. Entries can change while a directory is being read, so the map is a view of what the scanner observed during that run. Reparse points discovered during enumeration and unreadable entries are counted as skipped.

## From review to Recycle Bin

The review flow checks the selected path and file identity. Folders also get a fingerprint of descendant identities and timestamps. The app rechecks these before transfer, then asks Windows to recycle the verified item through `ITransferSource::RecycleItem`. It applies a change to the map when the Shell confirms it and prompts for a rescan when the result is uncertain.

The Windows transfer uses a pathname. A file can still change between the last identity check and the Shell operation. The verification fixture exercises replacement before that boundary and simulates a confirmed transfer to check the resulting map state. [Windows RecycleItem](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-itransfersource-recycleitem)
