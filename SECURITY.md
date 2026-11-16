# Security

Use [GitHub’s private vulnerability reporting form](https://github.com/tiwaritarush/disk-bubbles/security/advisories/new) for issues that could expose private data, bypass a protection rule, or move the wrong item. Include the app version, Windows version, the file system involved, and a reproduction that uses disposable files. Redact any personal filenames or paths.

Disk Bubbles runs with the current Windows user’s permissions. The Recycle Bin flow needs explicit review and confirmation. It validates eligible targets and uses the Windows Shell to request recycling. See [the architecture notes](docs/architecture.md) for what the file-identity checks cover and where the Windows handoff remains sensitive to concurrent file changes.
