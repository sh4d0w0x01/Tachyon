using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MftSearchWpf.Models;
using MftSearchWpf.Services;

namespace MftSearchWpf.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        // 1. Decouple the Data: Store millions of records here, NOT bound to UI
        private List<FileRecord> _allRecords = new List<FileRecord>();

        // 2. Empty Start: This is bound to the DataGrid/ListView and starts completely empty
        private ObservableCollection<FileRecord> _filteredRecords = new ObservableCollection<FileRecord>();

        private string _searchQuery = string.Empty;
        private string _statusText = "Initializing...";
        private bool _isBusy = true;
        private CancellationTokenSource? _searchCts;

        public MainViewModel()
        {
            if (!MftEngine.IsAdministrator())
            {
                MessageBox.Show("Please run this application as Administrator.", "Admin Rights Required", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

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

                sw.Stop();
                StatusText = $"Indexed {_allRecords.Count:N0} files in {sw.ElapsedMilliseconds} ms. Ready.";

                // Ensure the UI collection remains empty on startup
                Application.Current.Dispatcher.Invoke(() =>
                {
                    FilteredRecords.Clear();
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

            if (string.IsNullOrWhiteSpace(query))
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    FilteredRecords.Clear();
                });
                return;
            }

            try
            {
                // 3. Filter on Demand: Run filter on a background thread
                var results = await Task.Run(() =>
                {
                    return _allRecords
                        .Where(r => r.FileName.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .Take(200) // Take only the first 200 matches
                        .ToList();
                }, token);

                if (!token.IsCancellationRequested)
                {
                    // Push those 200 results into the ObservableCollection using the UI Dispatcher
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        FilteredRecords.Clear();
                        foreach (var record in results)
                        {
                            FilteredRecords.Add(record);
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when typing fast
            }
        }
    }
}
