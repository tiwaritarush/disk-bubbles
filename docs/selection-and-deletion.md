# Selection and deletion

Checkboxes mark items for cleanup. Their whole column is a click target, and Space marks the selected row. Protected items (critical Windows folders, system files, app-managed locations, folders with unreadable contents) cannot be marked; the disabled checkbox explains why. Marks persist while you browse the current scan and appear on the map as an accent ring with a check badge. The footer tray shows how much is marked and offers **Review & delete**.

The bubble menu offers Open (folders), Mark for cleanup, Move to Recycle Bin and Open in File Explorer. Move to Recycle Bin acts on that one item without a separate review.

## Review

Review collapses nested marks under a marked parent, then checks each item against the disk as it is now: the path must still name a plain local file or folder (no link or alias), the type must match the scan, a file's size must match, and its volume and file identity are recorded. Review never re-scans folder contents, so it is instant for folders of any size.

## Move

`RecycleBin.MoveAsync` runs on its own STA thread. Just before the move it re-reads each item's identity; a file edited since review or a folder replaced by another of the same name fails with "changed after review". All remaining items go to one `IFileOperation` with Recycle Bin semantics (`FOFX_RECYCLEONDELETE`, `FOF_ALLOWUNDO`) and an `IFileOperationProgressSink`. Windows reports each item through `PostDeleteItem`:

- a success with a new Recycle Bin item is **moved to the Recycle Bin**;
- a success without one is **permanently deleted** — this only happens after the user accepts the standard Windows prompt for an item too large for the Bin (`FOF_WANTNUKEWARNING`);
- a failure keeps the item and maps the copy-engine code from the Windows SDK's `sherrors.h` to a plain reason, such as an app holding a file open or access denied.

Anything Windows did not report is settled from the filesystem, so the app never claims a result it cannot see. Removed items leave the in-memory scan, ancestor totals update, and marks inside them clear.

## Results

The Deleted page appears whenever at least one item was removed. It shows the removed size, how many were recycled versus permanently deleted, and lists items that stayed with their reasons. Recycled items still use drive space until the Recycle Bin is emptied, and the page says so. If nothing was removed, a results dialog lists every item and why.

While Windows works, a progress sheet shows the item, file and size counts. Windows examines every file in a folder before recycling it, so very large folders take a moment; the sheet keeps moving the whole time.

Windows uses system attributes for [ordinary folder customization](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-customize-folders-with-desktop-ini) as well as operating-system files. A customized user folder and its `desktop.ini` or `Thumbs.db` metadata do not block removal by themselves.

Explorer navigation resolves a filesystem path to a Shell item ID on a separate STA thread and selects files with [SHOpenFolderAndSelectItems](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shopenfolderandselectitems).

## Completion motion

The bin and bubble scene is drawn with native WPF vectors and lasts 4.6 seconds. The actions are available immediately. Rendering stops when finished or hidden, and Windows reduced-motion settings show a static scene.

Verification moves real fixture files and a fixture folder to the Recycle Bin, holds one file open from another handle to confirm it stays with a clear reason, edits a file after review, and replaces a folder after review.
