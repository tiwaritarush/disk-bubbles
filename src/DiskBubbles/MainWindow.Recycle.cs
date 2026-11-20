using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using IOPath = System.IO.Path;

namespace DiskBubbles
{
    internal sealed partial class MainWindow
    {
        private sealed class MarkedEntry
        {
            public DiskEntry Entry;
            public DiskEntry[] Parents;
            public string Folder;
            public DiskEntry Parent => Parents[Parents.Length - 1];
        }

        private static readonly IComparer<DiskEntry> LargestFirst = Comparer<DiskEntry>.Create(DiskScanner.CompareEntries);
        private readonly Dictionary<DiskEntry, MarkedEntry> _marked = new Dictionary<DiskEntry, MarkedEntry>();

        private void SetMarked(DiskEntry entry, bool marked)
        {
            if (_trail.Count == 0)
                return;
            entry.IsMarked = marked;
            if (marked)
                _marked[entry] = new MarkedEntry { Entry = entry, Parents = _trail.ToArray(), Folder = CurrentPath() };
            else
                _marked.Remove(entry);
            UpdateMarks();
            UiMotion.Acknowledge(_selectionTray);
        }

        private void ClearMarks()
        {
            foreach (var entry in _marked.Keys)
                entry.IsMarked = false;
            _marked.Clear();
            UpdateMarks();
        }

        /// <summary>Marked items that are not already covered by a marked parent folder.</summary>
        private IEnumerable<MarkedEntry> MarkedRoots() => _marked.Values.Where(mark => !mark.Parents.Any(_marked.ContainsKey));

        private void UpdateMarks()
        {
            var roots = MarkedRoots().ToArray();
            long bytes = roots.Aggregate(0L, (total, mark) => DiskScanner.AddBytes(total, mark.Entry.Bytes));
            _selectionCount.Text = CountLabel(roots.Length, "item") + " marked";
            _selectionSize.Text = DiskEntry.FormatSize(bytes);
            _reviewRecycle.IsEnabled = _clearMarks.IsEnabled = roots.Length > 0 && !_recycleBusy;
            UiMotion.Reveal(_selectionTray, roots.Length > 0);
            UpdateSelection();
        }

        private Task ReviewRecycleAsync() => _marked.Count == 0 ? Task.CompletedTask : RecycleAsync(MarkedRoots().ToArray(), review: true);

        private async Task RecycleAsync(MarkedEntry[] selection, bool review)
        {
            if (_recycleBusy || _scan != null || selection.Length == 0)
                return;
            if (_result == null || selection.Any(mark => mark.Parents.Length == 0 || mark.Parents[0] != _result.Root))
                return;

            bool refreshed = false;
            using var cancellation = new CancellationTokenSource();
            _recycleCheck = cancellation;
            SetRecycleBusy(true);
            _work.Show("Checking items", "Esc to cancel");
            try
            {
                List<RecycleItem> items = await Task.Run(() => selection.GroupBy(mark => mark.Parent)
                    .SelectMany(group => RecycleItem.Prepare(group.First().Folder, group.Select(mark => mark.Entry), cancellation.Token))
                    .ToList());
                cancellation.Token.ThrowIfCancellationRequested();
                _recycleCheck = null;
                if (_closed)
                    return;
                if (items.Any(item => item.NeedsRescan))
                    _result.NeedsRefresh = true;

                RecycleItem[] ready = items.Where(item => item.Ready).ToArray();
                if (review)
                {
                    _work.Hide();
                    DiskEntry current = _trail[_trail.Count - 1];
                    DiskEntry projection = selection.All(mark => mark.Parents.Contains(current)) ? current : null;
                    if (new RecycleReview(this, items, projection, _result.Root).ShowDialog() != true)
                        return;
                }
                else if (ready.Length == 0)
                {
                    _work.Hide();
                    new RecycleReview(this, items, null, null, completed: true).ShowDialog();
                    return;
                }

                _movingToBin = true;
                long readyBytes = ready.Aggregate(0L, (total, item) => DiskScanner.AddBytes(total, item.Entry.Bytes));
                long readyFiles = ready.Sum(item => item.Files);
                string scope = $"{CountLabel(ready.Length, "item")} · {CountLabel(readyFiles, "file")} · {DiskEntry.FormatSize(readyBytes)}";
                _work.Show("Moving to the Recycle Bin", scope + (readyFiles > 5000 ? "\nWindows checks every file first, so large folders take a moment." : ""));
                var progress = new Progress<RecycleProgress>(step =>
                {
                    double fraction = step.Total == 0 ? 0 : Math.Max(step.Fraction, (double)step.Done / step.Total);
                    string detail = step.Current == null ? scope : $"{step.Done:N0} of {step.Total:N0} done · {step.Current}";
                    _work.Show("Moving to the Recycle Bin", detail, fraction > 0.001 ? fraction : (double?)null);
                });
                await RecycleBin.MoveAsync(ready, new WindowInteropHelper(this).Handle, progress);
                _movingToBin = false;
                _work.Hide();

                RecycleItem[] removed = ready.Where(item => item.Removed).ToArray();
                RecycleItem[] stayed = ready.Where(item => !item.Removed).ToArray();
                if (stayed.Any(item => item.NeedsRescan) || (removed.Length > 0 && ScannedRecycleBin()))
                    _result.NeedsRefresh = true;
                if (removed.Length > 0)
                {
                    ApplyRecycled(removed, selection);
                    refreshed = true;
                    ShowRecycleSuccess(removed, stayed);
                }
                else if (stayed.Length > 0)
                {
                    new RecycleReview(this, stayed, null, null, completed: true).ShowDialog();
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception error) when (RecycleBin.IsItemError(error))
            {
                if (!_closed)
                    UiVisuals.Notice(this, "Couldn’t check these items", error.Message).ShowDialog();
            }
            finally
            {
                _recycleCheck = null;
                _movingToBin = false;
                if (!_closed)
                {
                    _work.Hide();
                    SetRecycleBusy(false);
                    if (!refreshed)
                        UpdateScanStatus();
                }
            }
        }

        private void SetRecycleBusy(bool busy)
        {
            _recycleBusy = busy;
            _appsMode.IsEnabled = _diskMode.IsEnabled = !busy;
            _browser.IsEnabled = _folder.IsEnabled = _drive.IsEnabled = !busy;
            UpdateMarks();
        }

        private void ShowRecycleSuccess(IReadOnlyList<RecycleItem> removed, IReadOnlyList<RecycleItem> stayed)
        {
            if (removed.Count == 0)
                return;
            _completed.ShowResult(removed, stayed);
            ShowPage(_completed);
        }

        private bool ScannedRecycleBin() =>
            string.Equals(_rootPath, IOPath.GetPathRoot(_rootPath), StringComparison.OrdinalIgnoreCase) &&
            _result.Root.Children.Any(entry => entry.Name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase));

        /// <summary>Removes confirmed items from the in-memory scan and updates every affected total.</summary>
        private void ApplyRecycled(IReadOnlyList<RecycleItem> moved, IReadOnlyList<MarkedEntry> selection)
        {
            var removed = new HashSet<DiskEntry>(moved.Select(item => item.Entry));
            var changes = new Dictionary<DiskEntry, (int Depth, long Bytes, HashSet<DiskEntry> Children)>();
            foreach (var mark in selection.Where(mark => removed.Contains(mark.Entry)))
            {
                for (int depth = 0; depth < mark.Parents.Length; depth++)
                {
                    DiskEntry folder = mark.Parents[depth];
                    if (!changes.TryGetValue(folder, out var change))
                        change = (depth, 0L, new HashSet<DiskEntry>());
                    change.Bytes = DiskScanner.AddBytes(change.Bytes, mark.Entry.Bytes);
                    change.Children.Add(depth + 1 == mark.Parents.Length ? mark.Entry : mark.Parents[depth + 1]);
                    changes[folder] = change;
                }
            }

            // Deepest folders first, so each parent re-sorts children whose sizes are already final.
            foreach (var item in changes.OrderByDescending(item => item.Value.Depth))
            {
                DiskEntry folder = item.Key;
                folder.OrderChildren();
                folder.Children.RemoveAll(item.Value.Children.Contains);
                foreach (var child in item.Value.Children)
                {
                    if (removed.Contains(child))
                        continue;
                    int index = folder.Children.BinarySearch(child, LargestFirst);
                    folder.Children.Insert(index < 0 ? ~index : index, child);
                }
                folder.Bytes = folder.Bytes == long.MaxValue
                    ? folder.Children.Aggregate(0L, (total, child) => DiskScanner.AddBytes(total, child.Bytes))
                    : Math.Max(0, folder.Bytes - item.Value.Bytes);
            }

            foreach (var mark in _marked.Values.Where(mark => removed.Contains(mark.Entry) || mark.Parents.Any(removed.Contains)).ToArray())
            {
                mark.Entry.IsMarked = false;
                _marked.Remove(mark.Entry);
            }
            _result.Files = Math.Max(0, _result.Files - moved.Sum(item => item.Files));
            _result.Folders = Math.Max(1, _result.Folders - moved.Sum(item => item.Folders));
            PruneHistory(_backHistory, removed);
            PruneHistory(_forwardHistory, removed);
            int removedLevel = _trail.FindIndex(removed.Contains);
            if (removedLevel >= 0)
                _trail = _trail.GetRange(0, removedLevel);
            _map.InvalidateLayout(changes.Keys);
            RefreshBrowser();
        }

        private static void PruneHistory(Stack<List<DiskEntry>> history, HashSet<DiskEntry> removed)
        {
            List<DiskEntry>[] valid = history.Where(path => !path.Any(removed.Contains)).Reverse().ToArray();
            history.Clear();
            foreach (List<DiskEntry> path in valid)
                history.Push(path);
        }
    }
}
