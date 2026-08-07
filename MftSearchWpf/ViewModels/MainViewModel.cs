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
        private List<FileRecord> _allRecords = new List<FileRecord>();
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
                _allRecords = await MftEngine.BuildIndexAsync();

                sw.Stop();
                StatusText = $"Indexed {_allRecords.Count:N0} files in {sw.ElapsedMilliseconds} ms. Ready.";

                // Show initial empty view or everything
                FilteredRecords = new ObservableCollection<FileRecord>();
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
                // Optionally clear or show all. With millions, probably better to clear.
                FilteredRecords = new ObservableCollection<FileRecord>();
                return;
            }

            try
            {
                // Run filter on background thread
                var results = await Task.Run(() =>
                {
                    return _allRecords
                        .Where(r => r.FileName.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .Take(500) // Limit UI rendering to top 500 for responsiveness
                        .ToList();
                }, token);

                if (!token.IsCancellationRequested)
                {
                    // Update ObservableCollection on UI Thread
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        FilteredRecords = new ObservableCollection<FileRecord>(results);
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
