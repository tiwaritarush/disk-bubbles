# Scan measurements

## November 20, 2026

The previous scanner used at most eight workers. On this PC, Windows reports no seek penalty for C:, and 32 workers completed the scan sooner. The new default raises the limit only when the storage query succeeds and reports no seek penalty. Unknown and seeking devices keep the previous limit.

The live C: workload contained about 3.41 million files and 541,000 folders on a PC with 24 logical processors. Other applications were running and changing files during these measurements. Treat these as local observations, not a speed guarantee for other drives.

| Measurement | Workers | Seconds |
| --- | ---: | ---: |
| First exploratory scan | 8 | 155.001 |
| Next exploratory scan | 16 | 106.060 |
| Next exploratory scan | 32 | 98.016 |
| Repeated exploratory scan | 8 | 111.529 |
| Fresh process after exploration | 32 | 74.333 |
| Fresh process after exploration | 8 | 126.215 |
| New build, automatic worker selection | 32 | 97.122 |

The warm eight-worker comparison took 111.529 seconds versus 98.016 seconds with 32 workers, about 12% less time. The separate-process pair showed a larger gain, but the difference between runs shows why the first scan should not be compared directly with a later cached scan. Both comparisons favored the higher worker limit on this machine.

The fixture verification compares a serial scan with a 32-worker scan, including every item's size and protection flags. It also covers cancellation and the existing Recycle Bin checks. A disposable 36-file fixture produced the same 156,576,684-byte total with one, eight and 32 workers.

## Run the benchmark

Build the app, then run this from the repository root in PowerShell:

```powershell
.\scripts\BenchmarkScan.ps1 -Path C:\ -Workers 8,32,32,8 -Repeats 2
```

Each scan runs in a fresh process. Output is CSV with worker count, elapsed seconds, files, folders, skipped entries and total logical bytes. Use `-Workers 0` to measure the app's automatic choice and `-ExecutablePath` to compare a previous build. The benchmark only reads the selected folder. Its compiled runner stays under `artifacts/benchmark/`.

Keep the target, permissions and background workload comparable. Repeat both settings and compare file counts as well as times. A larger workload or a warmer filesystem cache can change elapsed time independently of the code.
