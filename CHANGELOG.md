# Changelog

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
