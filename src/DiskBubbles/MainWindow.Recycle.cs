using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            if (!entry.CanMark || _trail.Count == 0)
                return;
            entry.IsMarked = marked;
            if (marked)
                _marked[entry] = new MarkedEntry { Entry = entry, Parents = _trail.ToArray(), Folder = CurrentPath() };
            else
                _marked.Remove(entry);
            UpdateMarks();
        }

        private void ClearMarks()
        {
            foreach (var entry in _marked.Keys)
                entry.IsMarked = false;
            _marked.Clear();
            _entries.Items.Refresh();
            UpdateMarks();
        }

        private IEnumerable<MarkedEntry> MarkedRoots() => _marked.Values.Where(mark => !mark.Parents.Any(_marked.ContainsKey));

        private void UpdateMarks()
        {
            long bytes = MarkedRoots().Aggregate(0L, (total, mark) => DiskScanner.AddBytes(total, mark.Entry.Bytes));
            _selectionSummary.Text = _marked.Count == 0 ? "No items marked" : $"{CountLabel(_marked.Count, "item")} marked · {DiskEntry.FormatSize(bytes)}";
            _reviewRecycle.IsEnabled = _clearMarks.IsEnabled = _marked.Count > 0;
            UpdateSelection();
        }

        private async Task ReviewRecycleAsync()
        {
            if (_recycleBusy || _scan != null || _marked.Count == 0)
                return;

            var selection = MarkedRoots().ToArray();
            bool refreshed = false;
            using var cancellation = new CancellationTokenSource();
            _recycleCheck = cancellation;
            SetRecycleBusy(true);
            _status.Text = "Checking selected items…  ·  Esc to cancel";
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
                DiskEntry current = _trail[_trail.Count - 1];
                DiskEntry projection = selection.All(mark => mark.Parents.Contains(current)) ? current : null;
                if (new RecycleReview(this, items, projection, _result.Root).ShowDialog() != true)
                    return;

                _movingToBin = true;
                _status.Text = "Moving reviewed items to the Recycle Bin…";
                var reviewed = items.Where(item => item.Ready).ToArray();
                List<RecycleItem> moved = await RecycleItem.MoveAsync(reviewed);
                _movingToBin = false;
                var failures = reviewed.Where(item => !item.Ready).ToArray();
                if (failures.Any(item => item.NeedsRescan) || (moved.Count > 0 && ScannedRecycleBin()))
                    _result.NeedsRefresh = true;
                if (moved.Count > 0)
                {
                    ApplyRecycled(moved, selection);
                    refreshed = true;
                    _status.Text += $"  ·  {moved.Count:N0} moved to Recycle Bin";
                }

                if (failures.Length > 0)
                    new RecycleReview(this, failures, _trail[_trail.Count - 1], _result.Root, completed: true).ShowDialog();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception error) when (DiskScanner.IsFileSystemError(error) || error is ArgumentException)
            {
                if (!_closed)
                    UiVisuals.Notice(this, "Can’t review these items", error.Message).ShowDialog();
            }
            finally
            {
                _recycleCheck = null;
                _movingToBin = false;
                if (!_closed)
                {
                    SetRecycleBusy(false);
                    if (!refreshed)
                        UpdateScanStatus();
                }
            }
        }

        private void SetRecycleBusy(bool busy)
        {
            _recycleBusy = busy;
            _browser.IsEnabled = _folder.IsEnabled = _drive.IsEnabled = !busy;
        }

        private bool ScannedRecycleBin() =>
            string.Equals(_rootPath, IOPath.GetPathRoot(_rootPath), StringComparison.OrdinalIgnoreCase) &&
            _result.Root.Children.Any(entry => entry.Name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase));

        private void ApplyRecycled(IReadOnlyList<RecycleItem> moved, IReadOnlyList<MarkedEntry> selection)
        {
            _entries.ItemsSource = null;
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
            _result.Files -= moved.Sum(item => item.Files);
            _result.Folders -= moved.Sum(item => item.Folders);
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
