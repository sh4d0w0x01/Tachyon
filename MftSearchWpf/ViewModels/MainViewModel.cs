using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MftSearchWpf.Models;
using MftSearchWpf.Services;

namespace MftSearchWpf.ViewModels
{
    public enum FilterCategory
    {
        All,
        Executables,
        Scripts,
        Archives,
        Logs
    }

    public class MainViewModel : ViewModelBase
    {
        // 1. Decouple the Data: Store millions of records here, NOT bound to UI
        private List<FileRecord> _allRecords = new List<FileRecord>();

        // 2. Empty Start: This is bound to the DataGrid/ListView and starts completely empty
        private ObservableCollection<FileRecord> _filteredRecords = new ObservableCollection<FileRecord>();

        private string _searchQuery = string.Empty;
        private string _statusText = "Initializing...";
        private bool _isBusy = true;
        private int _totalFiles = 0;
        private FilterCategory _currentFilter = FilterCategory.All;
        private CancellationTokenSource? _searchCts;

        public ICommand CopyPathCommand { get; } = null!;
        public ICommand OpenLocationCommand { get; } = null!;
        public ICommand CopyHashCommand { get; } = null!;
        public ICommand CheckVirusTotalCommand { get; } = null!;
        public ICommand ClearSearchCommand { get; } = null!;

        public MainViewModel()
        {
            if (!MftEngine.IsAdministrator())
            {
                MessageBox.Show("Please run this application as Administrator.", "Admin Rights Required", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            CopyPathCommand = new RelayCommand<FileRecord>(ExecuteCopyPath);
            OpenLocationCommand = new RelayCommand<FileRecord>(ExecuteOpenLocation);
            CopyHashCommand = new RelayCommand<FileRecord>(ExecuteCopyHash);
            CheckVirusTotalCommand = new RelayCommand<FileRecord>(ExecuteCheckVirusTotal);
            ClearSearchCommand = new RelayCommand<object>(_ => SearchQuery = string.Empty);

            // Start background indexing
            _ = InitializeMftAsync();
        }

        public ObservableCollection<FileRecord> FilteredRecords
        {
            get => _filteredRecords;
            set => SetProperty(ref _filteredRecords, value);
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    ExecuteSearchAsync(value);
                }
            }
        }

        public FilterCategory CurrentFilter
        {
            get => _currentFilter;
            set
            {
                if (SetProperty(ref _currentFilter, value))
                {
                    ExecuteSearchAsync(SearchQuery);
                }
            }
        }

        public int TotalFiles
        {
            get => _totalFiles;
            set => SetProperty(ref _totalFiles, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private async Task InitializeMftAsync()
        {
            IsBusy = true;
            StatusText = "Indexing MFT across all drives...";
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                // Background task builds the List<FileRecord>
                _allRecords = await MftEngine.BuildIndexAsync();

                TotalFiles = _allRecords.Count;
                sw.Stop();
                StatusText = $"Ready | {TotalFiles:N0} files indexed | Showing top 0 matches (Search time: 0 ms)";

                // Ensure the UI collection remains empty on startup
                Application.Current.Dispatcher.Invoke(() =>
                {
                    FilteredRecords = new ObservableCollection<FileRecord>();
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                StatusText = $"Error during indexing: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void ExecuteSearchAsync(string query)
        {
            if (_allRecords == null || _allRecords.Count == 0) return;

            // Cancel previous search
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            if (string.IsNullOrWhiteSpace(query) && CurrentFilter == FilterCategory.All)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    FilteredRecords = new ObservableCollection<FileRecord>();
                    StatusText = $"Ready | {TotalFiles:N0} files indexed | Showing top 0 matches (Search time: 0 ms)";
                });
                return;
            }

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                // 3. Filter on Demand: Run filter on a background thread
                var results = await Task.Run(() =>
                {
                    var matchedRecords = new List<FileRecord>(200);

                    var extSet = GetFilterExtensions(CurrentFilter);
                    bool hasExtFilter = extSet != null && extSet.Count > 0;
                    bool hasQuery = !string.IsNullOrWhiteSpace(query);

                    foreach (var r in _allRecords)
                    {
                        if (token.IsCancellationRequested)
                            token.ThrowIfCancellationRequested();

                        bool match = true;

                        if (hasQuery && !r.FileName.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            match = false;
                        }

                        if (match && hasExtFilter)
                        {
                            string ext = Path.GetExtension(r.FileName);
                            if (!extSet.Contains(ext))
                            {
                                match = false;
                            }
                        }

                        if (match)
                        {
                            // Fetch file info safely
                            try
                            {
                                var fi = new FileInfo(r.FullPath);
                                if (fi.Exists)
                                {
                                    r.Size = fi.Length;
                                    r.DateModified = fi.LastWriteTime;
                                    r.Extension = fi.Extension;
                                }
                            }
                            catch
                            {
                                // Ignore if cannot read file info
                            }

                            matchedRecords.Add(r);
                            if (matchedRecords.Count >= 200) // Take only the first 200 matches
                                break;
                        }
                    }
                    return matchedRecords;
                }, token);

                sw.Stop();

                if (!token.IsCancellationRequested)
                {
                    // Re-instantiate the ObservableCollection on the UI thread to trigger only a single PropertyChanged event
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        FilteredRecords = new ObservableCollection<FileRecord>(results);
                        StatusText = $"Ready | {TotalFiles:N0} files indexed | Showing top {results.Count} matches (Search time: {sw.ElapsedMilliseconds} ms)";
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when typing fast
            }
        }

        private HashSet<string>? GetFilterExtensions(FilterCategory category)
        {
            return category switch
            {
                FilterCategory.Executables => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".dll", ".sys" },
                FilterCategory.Scripts => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ps1", ".py", ".sh", ".bat" },
                FilterCategory.Archives => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z", ".tar" },
                FilterCategory.Logs => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".log", ".txt", ".json" },
                _ => null
            };
        }

        private void ExecuteCopyPath(FileRecord record)
        {
            if (record != null && !string.IsNullOrEmpty(record.FullPath))
            {
                Clipboard.SetText(record.FullPath);
                StatusText = "Path copied to clipboard.";
            }
        }

        private void ExecuteOpenLocation(FileRecord record)
        {
            if (record != null && !string.IsNullOrEmpty(record.FullPath))
            {
                try
                {
                    // Wrap the path in quotes and use /select to securely open Explorer without executing the file
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{record.FullPath}\"",
                        UseShellExecute = false
                    });
                }
                catch (Exception ex)
                {
                    StatusText = $"Failed to open location: {ex.Message}";
                }
            }
        }

        private async void ExecuteCopyHash(FileRecord record)
        {
            if (record != null && !string.IsNullOrEmpty(record.FullPath))
            {
                try
                {
                    StatusText = "Calculating SHA256...";
                    string hash = await CalculateSha256Async(record.FullPath);
                    Clipboard.SetText(hash);
                    StatusText = "SHA256 copied to clipboard.";
                }
                catch (Exception ex)
                {
                    StatusText = $"Hash failed: {ex.Message}";
                }
            }
        }

        private async void ExecuteCheckVirusTotal(FileRecord record)
        {
            if (record != null && !string.IsNullOrEmpty(record.FullPath))
            {
                try
                {
                    StatusText = "Calculating SHA256 for VirusTotal...";
                    string hash = await CalculateSha256Async(record.FullPath);
                    string url = $"https://www.virustotal.com/gui/search/{hash}";

                    // Safely open the browser
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                    StatusText = "Opened VirusTotal in browser.";
                }
                catch (Exception ex)
                {
                    StatusText = $"VirusTotal check failed: {ex.Message}";
                }
            }
        }

        private Task<string> CalculateSha256Async(string filePath)
        {
            return Task.Run(() =>
            {
                // Safely read file bytes without locking it
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sha256 = SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            });
        }
    }
}
