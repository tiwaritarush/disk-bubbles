# Changelog

## 11.0.0

### Deletion you can trust

- Fix deletion reporting "Windows did not confirm the move" for items it had in fact moved, and never reaching the Deleted page. The old code called `ITransferSource::RecycleItem` directly and treated Windows' non-zero success codes as failures.
- Move items with `IFileOperation`, the engine File Explorer uses, in one batch. Windows reports every item: moved to the Recycle Bin, permanently deleted, or kept, with a plain reason (in use by another app, access denied, changed or missing since review).
- Remove the hang before deleting. Review no longer re-scans each selected folder and opens every file inside it twice; it checks the selected item itself, so a 40,000-file folder reviews in milliseconds instead of minutes.
- Show a progress sheet while Windows works, with item, file and size counts. Large folders still take Windows a moment; the app never looks frozen.
- Items too large for the Recycle Bin bring up the standard Windows permanent-delete prompt. Declining keeps the item and the result says why.
- The Deleted page separates recycled from permanently deleted items, lists anything that stayed with its reason, and adds **Open Recycle Bin**.
- A file edited after review, or a folder replaced by another of the same name, is not moved.

### Space Lens map

- Bubble area is exactly proportional to size within a page, packed with a front-chain circle packer into a cluster shaped to the map.
- Folders preview their largest contents as smaller bubbles inside them.
- A page holds as many items as stay legible. The rest gather into a **+N more** bubble and a pager with a clear **Next page** button. Page Up and Page Down also move between pages.
- Selecting a row turns to the page that shows its bubble.

### Liquid glass

- A soft ambient light field sits behind translucent glass panes with specular rims, in both themes.
- Bubbles are clearer glass with the content's colour pooled inside: folders in the theme's green or pink, with subtle tints for video, images, audio, archives, apps and documents. List rows carry the same tint.
- The light behind the map shifts toward the colour of the bubble under the pointer.
- Marked bubbles get an accent ring and check badge. The footer shows a selection tray with the marked size and a **Review & delete** button.
- Glass buttons, round navigation buttons, keyboard-only focus rings, and refreshed start, scanning and empty states.

### Other

- Protected items can no longer be marked; their checkbox is disabled and explains why.
- Bubble menu: Open, Mark for cleanup, Move to Recycle Bin, Open in File Explorer.
- Space marks the selected row, Delete reviews it, Backspace goes up a folder.
- Verification now moves real fixture files to the Recycle Bin, including a file held open by another process, and checks true-to-size bubble geometry.

## 10.5.1

- Allow customized Downloads and other ordinary folders to pass deletion checks when they contain `desktop.ini` or thumbnail metadata. Keep critical Windows paths, app-managed locations, system files and unverified links protected.
- Open filesystem locations through Windows Shell item IDs instead of Explorer command-line arguments. Select files precisely, handle special characters, and report missing or changed locations without falling back to Home.
- Verify customized-folder review and native Shell path resolution, including the actual Downloads folder and filenames containing commas and Unicode.

## 10.5.0

- Make the whole checkbox column toggle selection and update checkmarks when a menu action selects an item.
- Retain the last hovered bubble's row highlight and scroll its row into view. Protected items can be selected for review while deletion remains blocked.
- Add Select and Delete above Open in Explorer. Delete validates and recycles only the clicked item.
- Show confirmed removal totals with a native clay bin and glass bubble animation, plus Back to selection and Home screen. Stop rendering when hidden and respect reduced motion.
- Keep the disk list themed while actions are busy. Reject deletion commands from an outdated scan.

## 10.4.3

- Right-click a bubble to open its context menu and choose Open in Explorer. Folders open directly; files are selected in their containing folder.
- Opening the menu preserves the current folder and Recycle Bin marks. The menu uses the item's path from when it was opened.

## 10.4.2

- Center the checkbox tick inside its outline in Disk Scan and App Cleaner. The surrounding click area belongs to the checkbox.
- Increase scan concurrency on storage that Windows reports without a seek penalty. Keep the previous worker limit for seeking and unknown devices.
- Verify that serial and parallel scans produce the same tree, sizes and protection flags. Add a repeatable scan benchmark and record the measurements.

## 10.4.1

- Remove confirmed uninstalls from the current app list without a full scan.
- Keep the app list themed while uninstall and cleanup actions are in progress.

## 10.4.0

- Put Uninstall and Deep Uninstall in the App Cleaner details pane and extend both panes into the former footer.
- Run verified Desktop uninstallers and supported Store removal from App Cleaner.
- Review confirmed install leftovers after removal. Deep Uninstall also looks for matching app data and caches. Cleanup checks registered apps, services and driver files before recycling.

## 10.3.8

- Center the toolbar icons in equally sized buttons, with consistent spacing and inset keyboard focus.

## 10.3.7

- Give the mode switch one shared outline, with a filled active tab and an inset keyboard focus indicator.
- Use neutral charcoal in dark mode and keep the green reflections inside the bubbles.

## 10.3.6

- Give the top toolbar the same frosted glass as the results panes, and make file names, sizes, and breadcrumb labels easier to read.
- Widen the file pane and use more of the storage map for bubbles while keeping room for every map page.
- Keep the whole checkbox area separate from folder navigation so marking or unmarking stays in the same folder.

## 10.3.5

- Put the file list, storage map, and App Cleaner results and details in rounded glass panes.
- Soften the bubble reflections and diffuse their color within the glass.

## 10.3.4

- Keep pink and green reflections inside the glass bubbles and remove the ambient glow from the backgrounds.

## 10.3.3

- Use cream for the light workspace and ink for primary controls and folder icons.
- Give the storage map and App Cleaner translucent panes and turn the pink and green tint into a soft reflection along the glass.

## 10.3.2

- Spread the pink and green blushes across the storage map and reflect them in the glass.
- Show which part of the app scan is running and let you cancel from the progress screen.
- Display registered Desktop and Store icons in App Cleaner when Windows makes them available.

## 10.3.1

- Center the start and scan screens, bring both modes into one toolbar, and give App Cleaner one shared surface for the list and details.

## 10.3.0

- Open App Cleaner separately from Disk Scan. Compare measured install folders, filter apps, and review uninstall actions.
- Inspect folders left after uninstall. Folder moves use the same Recycle Bin review as Disk Scan, with a fresh check that no installed app claims the folder.
- Increase text and control sizes, preview the storage map on the start page, and spread the green and pink blushes behind the glass.

## 10.2.0

- Refresh the light theme with cream and ink and add a soft orchid reflection to the map. The dark theme uses midnight and ice white with a green aurora behind the bubbles.
- Line up storage figures, give the map arrows larger targets, and keep the layout steady as you browse or mark items.
- Make the return zoom quicker and add subtle press and mark feedback when Windows animations are enabled.

## 10.1.2

- Keep the first folder zoom playing when the map changes size as you leave the drive overview.

## 10.1.1

- Folder zooms start after the file list lays out. The old and new maps move and fade together using cached artwork.
- Smaller bubbles fit into the gaps between larger ones, creating a tighter cluster across folders and map pages.

## 10.1.0

- Open folders with one click in the list or on the map. The zoom follows the bubble, with shorter transitions and a reflection that follows the pointer.
- Mark files and folders across the current scan, then review them together from the trash icon in the footer. Parent folders cover their marked descendants in review.
- Page through every item on the map, including small and zero-byte entries. Going back restores the last map page and list scroll position.

The disposable fixture verifies the review list and simulates a confirmed move across folders. Windows denied the live Shell transfer in the test environment and the disposable file remained in place.

## 10.0.1

First GitHub preview. Includes the portable Windows executable, published source, screenshots, and a reproducible build and verification workflow. The recycle guard also checks that the resolved filename retains the exact spelling and casing selected in the app.

The verification fixture exercises both themes, navigation, scan cancellation, the recycle review dialog, and rejection of protected or replaced targets. In the local smoke test, Windows refused to recycle a disposable file and the original remained. A successful live Recycle Bin transfer still needs verification.

## Earlier milestones

- **10.0.0** added file identity checks for descendants of reviewed folders, a Shell identity check at handoff, and deferred sorting for smaller folders. The C: scan engine processed 3.47 million files and folders in 40.3 seconds in one live run. Drive contents and caching affect the result.
- **9.0.0** introduced the frosted glass material for light mode.
- **8.0.0** added the beige theme, refined folder and dialog motion, and reserved the **System** badge for critical Windows folder roots.
- **7.0.0** moved the file list to the left and rebuilt the bubble spacing, hover details, and background navigation.
