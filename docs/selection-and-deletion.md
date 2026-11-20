# Selection and deletion

Checkboxes select items for review. Their full column is a click target, and the model notifies the list when a context-menu action changes a mark. Selection is allowed for protected items so the checkbox always responds; deletion still checks protection, file identity and folder contents. A protected item's Delete command is disabled, and the review explains why it must stay in place.

The last bubble hovered keeps its matching row highlighted. A different bubble replaces that highlight; changing folders clears it. Highlighting scrolls the matching row into view. It does not mark the item for deletion.

Bubble menus contain Select, Delete and Open in Explorer, in that order. Select marks and reveals the matching row. Delete validates only the clicked item and moves it to the Recycle Bin without requiring a checkbox or a separate selection review. The normal trash action still reviews cumulative marks. Both paths use the existing identity and protection checks.

The completion page appears only for confirmed transfers. Its size and item count come only from the items Windows confirmed as moved. Moving to the Recycle Bin does not immediately free that space on the volume; the page names the destination rather than claiming free disk capacity. Failed items are reported separately and stay out of the success total.

Windows uses system attributes for [ordinary folder customization](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-customize-folders-with-desktop-ini) as well as operating-system files. A customized user folder and its `desktop.ini` or `Thumbs.db` metadata do not block removal by themselves. Critical path rules, system-managed roots, other system-marked files, registered app locations and link checks still apply during scanning and final identity verification.

Explorer navigation resolves a filesystem path to a Shell item ID on a separate STA thread. A folder opens through `ShellExecuteEx`; a file is selected through [SHOpenFolderAndSelectItems](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shopenfolderandselectitems). The resolved filesystem path must match the requested one. Missing paths and changed item types produce an error rather than a Home window. App Cleaner uses the same folder-opening path.

## Completion motion

The large central scene is drawn with native WPF vectors. The bin falls under acceleration, compresses on impact and rebounds. Its attached lid springs to 75% of a right angle. A green and pink glass bubble with eyes and a smile enters from below, circles upward in front of and behind the bin, then bursts above the opening. Every particle follows a curve into the open mouth. The bin contracts into a point after the particles arrive.

The sequence lasts 4.6 seconds. The two navigation actions are available immediately; users do not have to wait for the animation. Rendering stops on completion, when the page is hidden, or when its window unloads. Windows reduced-motion settings replace the movement with a static scene.

Off-screen verification covers both themes, compact windows, key animation poses, selection bindings, sticky hover, protected commands, confirmed size totals and both completion actions. Test captures use disposable fixture data.

![Native WPF deletion animation with a clay bin and glass bubble mascot](screenshots/deletion.gif)

![Completion screen showing confirmed removal totals and navigation actions](screenshots/deleted.png)
