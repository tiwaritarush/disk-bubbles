---
version: 1
slug: "src-diskbubbles-mainwindow-layout-cs"
primary_target: "src/DiskBubbles/MainWindow.Layout.cs"
related_targets: ["src/DiskBubbles/BubbleMap.cs"]
---

# Disk Scan browser

Scope: the Disk Scan surfaces of the Windows WPF app (start, scanning, browser with list + bubble map, review dialog, deletion progress sheet, Deleted page). Mode: Operate. App Cleaner inherits the same world without workflow changes.

Audience and task: a Windows user whose drive is filling up finds the big items within seconds, marks them, reviews, and moves them to the Recycle Bin with a definite per-item result.

## Direction contract

THESIS: Liquid glass you can read. The user pinned the incumbent world (glass bubbles with subtle colour inside); this build makes it real glass by giving it light to refract, and refuses the category default of a grey dashboard with a flat treemap.

OWN-WORLD: A soft ambient light field (dark: emerald, cobalt, violet on near-black; light: pink, peach, lilac on warm paper) behind translucent panes with a specular rim brightest top-left and a faint dark outline. Bubbles are nearly clear orbs: Fresnel-thick edges, content colour pooling at the bottom, a coloured refraction ring, a specular crescent and glint. Colour inside a bubble is its kind (folder in the theme's own green or pink; video violet, images amber, audio coral, archives sky, apps periwinkle, documents green or lemon). Segoe UI throughout, tabular numerals, one accent for primary actions, marks and focus.

STORY: The user sees what is big immediately because area is exactly proportional to size; folders show their makeup inside them; anything too small to read is gathered into a "+N more" bubble and a pager that says where the rest is. Marking builds a tray with a real "Review & delete" action; deleting shows progress and ends on a page that tells the truth about every item.

FIRST VIEWPORT: Glass toolbar (brand mark of three glass bubbles, Disk Scan / App Cleaner segmented control, scan/bin/theme icons). Breadcrumbs with round glass back/forward/up. Left pane: Contents list with kind-tinted glyph discs, checkboxes, sizes. Right pane (two thirds): folder name and caption, large total top right, the bubble cluster filling the pane, glass pager pill bottom centre with an accent Next page. Footer: drive meter left, counts, selection tray right.

FORM: Incumbent world, pinned by the user (assigned direction from the roll was offered and declined in favour of the pin). Seed key ce302b64.

Signature interaction: the ambient light behind the map shifts toward the colour of the bubble under the pointer, as if the bubble were lit from behind.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
