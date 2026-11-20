# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

> Note: the shipped app is a native Windows desktop application (C#, WPF, .NET Framework 4.8). `web` is recorded only because the schema has no Windows value; design work must follow Windows desktop conventions (Segoe UI, Fluent-style controls, title-bar theming, keyboard shortcuts, Per-Monitor DPI), not web ones.

## Users

People on a Windows PC whose drive is filling up and who want to find out what is using the space, then clear it out safely. They open the app occasionally, usually when Windows warns about low space or a download fails. (Inferred from the existing app and the user's brief; not interviewed.)

## Product Purpose

Disk Bubbles scans a drive or folder, shows what is taking space as a bubble map where area tracks size, lets the user drill into folders, mark files and folders, and move them to the Recycle Bin. App Cleaner inventories installed apps, runs their uninstallers and finds verified leftover folders.

Success: the user finds the big items within seconds of the scan finishing, and deletion either visibly succeeds or says exactly why an item stayed.

## Positioning

A Windows counterpart to CleanMyMac's Space Lens: the size of a bubble is the size on disk, folders preview their contents inside them, and deletion is reviewed, reversible (Recycle Bin) and honest about every item.

## Operating Context

- Portable single `.exe`, no installer, runs as the signed-in user (no elevation).
- Scans can cover millions of files; results live in memory for the session.
- Deletion goes to the Windows Recycle Bin. Items too large for the Bin trigger the standard Windows permanent-delete prompt; declining keeps the item and the app explains why.

## Capabilities and Constraints

- Disk Scan: drive or folder scan, bubble map with drill-down, list beside the map, cross-folder marking, review and Recycle Bin move, success screen.
- Bubble map follows Space Lens: largest items true to size, nested previews inside folders. When a folder has too many items to show legibly, the map pages, and the next page must be clearly signposted.
- App Cleaner: installed-app inventory, uninstall, Deep Uninstall leftovers. Its workflow stays; it adopts the new look and the shared deletion engine.
- No telemetry, no account, no network service.
- Protected Windows and app-managed locations are never offered for deletion.

## Brand Commitments

- Name: Disk Bubbles. Light and dark appearance, user switchable and remembered.
- The bubble is the product's mark and its core visual.

## Evidence on Hand

- Screenshots of v10.5.1: `docs/screenshots/`.
- Reference behavior: CleanMyMac 5 Space Lens feature map (user-supplied analysis).
- No customer quotes, benchmarks against competitors, or usage numbers exist; none may be invented.

## Product Principles

1. Truth over reassurance: never claim a delete that Windows did not perform, never hide one that it did.
2. Size is the interface: what is big must look big, immediately.
3. Never strand the user: long work shows progress and can be cancelled; nothing appears frozen.
4. Reversible by default: Recycle Bin first; permanent deletion only through an explicit Windows prompt.

## Accessibility & Inclusion

- Keyboard path for every map action via the list; Windows reduced-motion preference respected; WCAG AA text contrast in both themes.
