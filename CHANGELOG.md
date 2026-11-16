# Changelog

## 10.0.1

First GitHub preview. Includes the portable Windows executable, published source, screenshots, and a reproducible build and verification workflow. The recycle guard also checks that the resolved filename retains the exact spelling and casing selected in the app.

The verification fixture exercises both themes, navigation, scan cancellation, the recycle review dialog, and rejection of protected or replaced targets. In the local smoke test, Windows refused to recycle a disposable file and the original remained. A successful live Recycle Bin transfer still needs verification.

## Earlier milestones

- **10.0.0** added file identity checks for descendants of reviewed folders, a Shell identity check at handoff, and deferred sorting for smaller folders. The C: scan engine processed 3.47 million files and folders in 40.3 seconds in one live run. Drive contents and caching affect the result.
- **9.0.0** introduced the frosted glass material for light mode.
- **8.0.0** added the beige theme, refined folder and dialog motion, and reserved the **System** badge for critical Windows folder roots.
- **7.0.0** moved the file list to the left and rebuilt the bubble spacing, hover details, and background navigation.
