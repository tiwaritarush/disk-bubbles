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
            UiMotion.Acknowledge(_selectionSummary);
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
            if (_reviewRecycle.Content is System.Windows.Shapes.Shape binIcon)
                binIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, _marked.Count > 0 ? "AccentInk" : "Muted");
            if (_marked.Count > 0)
                UiVisuals.Emphasize(_reviewRecycle);
            else
            {
                _reviewRecycle.ClearValue(BackgroundProperty);
                _reviewRecycle.ClearValue(BorderBrushProperty);
                _reviewRecycle.ClearValue(ForegroundProperty);
            }
            _clearMarks.Visibility = _marked.Count > 0 ? Visibility.Visible : Visibility.Hidden;
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
            _status.Text = "Checking selected items…  ·  Esc to cancel";
            try
            {
                // Review checks each selected item itself; it never re-scans folder contents, so it is instant.
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
                if (review && new RecycleReview(this, items, projection, _result.Root).ShowDialog() != true)
                    return;

                RecycleItem[] ready = items.Where(item => item.Ready).ToArray();
                if (ready.Length > 0)
                {
                    _movingToBin = true;
                    long files = ready.Sum(item => item.Files);
                    string scope = $"{CountLabel(ready.Length, "item")} · {CountLabel(files, "file")}";
                    _status.Text = $"Moving to the Recycle Bin…  ·  {scope}";
                    var progress = new Progress<RecycleProgress>(step =>
                    {
                        double fraction = step.Total == 0 ? 0 : Math.Max(step.Fraction, (double)step.Done / step.Total);
                        _status.Text = $"Moving to the Recycle Bin…  ·  {step.Done:N0} of {step.Total:N0} done" +
                            (fraction > 0.001 ? $"  ·  {fraction:P0}" : $"  ·  {scope}");
                    });
                    await RecycleBin.MoveAsync(ready, new WindowInteropHelper(this).Handle, progress);
                    _movingToBin = false;
                }

                RecycleItem[] moved = ready.Where(item => item.Removed).ToArray();
                // A direct Delete has no review, so an item that failed its check is reported here too.
                RecycleItem[] failures = (review ? ready : items.ToArray()).Where(item => !item.Removed).ToArray();
                if (failures.Any(item => item.NeedsRescan) || (moved.Length > 0 && ScannedRecycleBin()))
                    _result.NeedsRefresh = true;
                if (moved.Length > 0)
                {
                    ApplyRecycled(moved, selection);
                    refreshed = true;
                }

                if (failures.Length > 0)
                    new RecycleReview(this, failures, _trail[_trail.Count - 1], _result.Root, completed: true).ShowDialog();
                if (moved.Length > 0)
                    ShowRecycleSuccess(moved);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception error) when (RecycleBin.IsItemError(error))
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
            _appsMode.IsEnabled = !busy;
            _diskMode.IsEnabled = !busy;
            _browser.IsEnabled = _folder.IsEnabled = _drive.IsEnabled = !busy;
        }

        private void ShowRecycleSuccess(IReadOnlyList<RecycleItem> moved)
        {
            if (moved.Count == 0)
                return;
            long bytes = moved.Aggregate(0L, (total, item) => DiskScanner.AddBytes(total, item.Entry.Bytes));
            _completed.ShowResult(bytes, moved.Count, moved.Count(item => item.Outcome == RecycleOutcome.Deleted));
            ShowPage(_completed);
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
            _result.Files = Math.Max(0, _result.Files - moved.Sum(item => item.Files));
            _result.Folders = Math.Max(1, _result.Folders - moved.Sum(item => item.Folders));
            PruneHistory(_backHistory, removed);
            PruneHistory(_forwardHistory, removed);
            int removedLevel = _trail.FindIndex(removed.Contains);
            if (removedLevel >= 0)
                _trail = _trail.GetRange(0, removedLevel);
            _map.InvalidateLayout(changes.Keys);
            RefreshBrowser();
            _status.Text += $"  ·  {moved.Count:N0} removed";
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
