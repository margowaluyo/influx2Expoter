using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using ClosedXML.Excel;
using InfluxDB.Client;
using InfluxDB.Client.Api;

namespace influx2Exporter.ViewModels
{
 public class MainViewModel : INotifyPropertyChanged
 {
 private string _host = string.Empty, _org = string.Empty, _token = string.Empty, _bucket = string.Empty, _port = string.Empty;
 private string _connectionStatus = "Disconnected", _displayStatus = "Disconnected";
 private bool _isConnected, _isQueryMode, _isQueryBusy;
 private DateTime? _fromDate = DateTime.UtcNow.AddHours(-1);
 private string _fromTime = "00:00:00";
 private DateTime? _toDate = DateTime.UtcNow;
 private string _toTime = "23:59:59";
 private DataTable? _previewTable;
 private string _lastSchemaError = string.Empty;
 // New DateTimePicker bound values
 private DateTime? _fromDateTime;
 private DateTime? _toDateTime;

 // properties
 public string Host { get => _host; set { if (SetProperty(ref _host, value)) RaiseCanExec(); } }
 public string Org { get => _org; set { if (SetProperty(ref _org, value)) RaiseCanExec(); } }
 public string Token { get => _token; set { if (SetProperty(ref _token, value)) RaiseCanExec(); } }
 public string Bucket { get => _bucket; set { if (SetProperty(ref _bucket, value)) RaiseCanExec(); } }
 public string Port { get => _port; set { if (SetProperty(ref _port, value)) RaiseCanExec(); } }
 public string ConnectionStatus { get => _connectionStatus; set { if (SetProperty(ref _connectionStatus, value)) { } } }
 public string DisplayStatus { get => _displayStatus; set => SetProperty(ref _displayStatus, value); }
 public bool IsConnected { get => _isConnected; set { if (SetProperty(ref _isConnected, value)) RaiseCanExec(); } }
 public bool IsQueryMode { get => _isQueryMode; set { if (SetProperty(ref _isQueryMode, value)) { if (_isQueryMode && Filters.Count ==0) _ = LoadFiltersAsync(); } } }
 public bool IsQueryBusy { get => _isQueryBusy; set { if (SetProperty(ref _isQueryBusy, value)) RaiseCanExec(); } }
 public string LastSchemaError { get => _lastSchemaError; private set => SetProperty(ref _lastSchemaError, value); }

 // Legacy time controls
 public DateTime? FromDate { get => _fromDate; set => SetProperty(ref _fromDate, value); }
 public string FromTime { get => _fromTime; set => SetProperty(ref _fromTime, value); }
 public DateTime? ToDate { get => _toDate; set => SetProperty(ref _toDate, value); }
 public string ToTime { get => _toTime; set => SetProperty(ref _toTime, value); }
 // New datetime pickers
 public DateTime? FromDateTime { get => _fromDateTime; set => SetProperty(ref _fromDateTime, value); }
 public DateTime? ToDateTime { get => _toDateTime; set => SetProperty(ref _toDateTime, value); }

 // schema filters fetched from server
 public ObservableCollection<QueryFilter> Filters { get; } = new();
 // available field names for panel ComboBox
 public ObservableCollection<string> AvailableFieldNames { get; } = new();
 // dynamic panels shown in UI
 public ObservableCollection<FilterPanel> Panels { get; } = new();

 // Preview data
 public DataTable? PreviewTable { get => _previewTable; private set { if (SetProperty(ref _previewTable, value)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewView))); } }
 public DataView? PreviewView => PreviewTable?.DefaultView;

 // commands
 public ICommand ConnectCommand { get; }
 public ICommand NewQueryCommand { get; }
 public ICommand OpenTemplateCommand { get; }
 public ICommand SubmitQueryCommand { get; }
 public ICommand AddFilterPanelCommand { get; }
 public ICommand RemoveFilterPanelCommand { get; }
 public ICommand ExportCsvCommand { get; }
 public ICommand ExportExcelCommand { get; }

 public MainViewModel()
 {
 // Enable reconnect always (only disabled while executing via AsyncRelayCommand internal state)
 ConnectCommand = new AsyncRelayCommand(ConnectAsync); // removed predicate !IsConnected
 NewQueryCommand = new SimpleRelayCommand(OnNewQuery, () => IsConnected);
 OpenTemplateCommand = new SimpleRelayCommand(() => { }, () => IsConnected);
 SubmitQueryCommand = new AsyncRelayCommand(SubmitQueryAsync, CanSubmitQuery);
 AddFilterPanelCommand = new SimpleRelayCommand(AddFilterPanel, () => IsConnected && Filters.Count >0);
 RemoveFilterPanelCommand = new RelayCommand<FilterPanel>(RemoveFilterPanel, p => p != null && Panels.Contains(p) && Panels.Count >1);
 ExportCsvCommand = new SimpleRelayCommand(ExportCsv, () => PreviewTable != null && PreviewTable.Rows.Count >0);
 ExportExcelCommand = new SimpleRelayCommand(ExportExcel, () => PreviewTable != null && PreviewTable.Rows.Count >0);
 }

 private bool CanSubmitQuery() => IsConnected && !IsQueryBusy && Filters.Any(f => f.Values.Any(v => v.IsSelected));
 private void OnNewQuery() => IsQueryMode = true;

 private void AddFilter(string name, System.Collections.Generic.IEnumerable<string> values) => AddFilterToCollection(name, values);
 private void AddFilterToCollection(string name, System.Collections.Generic.IEnumerable<string> values)
 { var f = new QueryFilter { Name = name }; foreach (var v in values.Distinct().OrderBy(s => s)) f.Values.Add(new QueryFilterValue { Value = v }); Filters.Add(f); RaiseCanExec(); }

 private void RebuildAvailableFields() { AvailableFieldNames.Clear(); foreach (var n in Filters.Select(f => f.Name)) AvailableFieldNames.Add(n); }
 private void EnsureAtLeastOnePanel() { if (Panels.Count ==0) { var defaultName = AvailableFieldNames.FirstOrDefault() ?? string.Empty; Panels.Add(new FilterPanel(this) { SelectedName = defaultName }); } }
 private void AddFilterPanel() { var defaultName = AvailableFieldNames.FirstOrDefault() ?? string.Empty; Panels.Add(new FilterPanel(this) { SelectedName = defaultName }); RaiseCanExec(); }
 private void RemoveFilterPanel(FilterPanel? panel) { if (panel == null || Panels.Count <=1) return; Panels.Remove(panel); RaiseCanExec(); }

 private async Task LoadFiltersAsync()
 {
 try
 {
 LastSchemaError = string.Empty;
 if (!IsConnected) { SetSchemaError("Not connected"); return; }
 if (string.IsNullOrWhiteSpace(Bucket)) { SetSchemaError("Bucket empty"); return; }
 using var influx = new InfluxDBClient(BuildBaseUri(Host, Port).ToString(), Token);
 var queryApi = influx.GetQueryApi();
 Filters.Clear(); Panels.Clear(); AvailableFieldNames.Clear();

 await SafeSchemaCall("measurements", async () =>
 {
 var measurements = await QueryFluxStringsAsync(queryApi, $"import \"influxdata/influxdb/schema\"\nschema.measurements(bucket: \"{Bucket}\")");
 AddFilter("_measurement", measurements);
 });
 await SafeSchemaCall("fields", async () =>
 {
 var fields = await QueryFluxStringsAsync(queryApi, $"import \"influxdata/influxdb/schema\"\nschema.fields(bucket: \"{Bucket}\")");
 AddFilter("_field", fields);
 });
 await SafeSchemaCall("tagKeys", async () =>
 {
 var tagKeys = await QueryFluxStringsAsync(queryApi, $"import \"influxdata/influxdb/schema\"\nschema.tagKeys(bucket: \"{Bucket}\")");
 foreach (var key in tagKeys.Take(6))
 {
 await SafeSchemaCall($"tagValues:{key}", async () =>
 {
 var values = await QueryFluxStringsAsync(queryApi, $"import \"influxdata/influxdb/schema\"\nschema.tagValues(bucket: \"{Bucket}\", tag: \"{key}\")");
 if (values.Count >0) AddFilter(key, values);
 });
 }
 });

 RebuildAvailableFields();
 EnsureAtLeastOnePanel();
 if (!string.IsNullOrEmpty(LastSchemaError))
 {
 Filters.Add(new QueryFilter { Name = "load_error", Values = { new QueryFilterValue { Value = LastSchemaError } } });
 }
 }
 catch (Exception ex)
 {
 SetSchemaError(ex.Message);
 }
 }

 private async Task SafeSchemaCall(string label, Func<Task> action)
 {
 try { await action(); }
 catch (Exception ex)
 {
 LastSchemaError += (LastSchemaError.Length >0 ? " | " : string.Empty) + $"{label}: {ex.Message}";
 }
 }
 private void SetSchemaError(string msg)
 {
 Filters.Clear(); Panels.Clear(); AvailableFieldNames.Clear();
 LastSchemaError = msg;
 Filters.Add(new QueryFilter { Name = "load_error", Values = { new QueryFilterValue { Value = msg } } });
 EnsureAtLeastOnePanel();
 }

 private async Task<System.Collections.Generic.List<string>> QueryFluxStringsAsync(IQueryApi queryApi, string flux)
 {
 var res = new System.Collections.Generic.List<string>();
 var tables = await queryApi.QueryAsync(flux, Org);
 foreach (var table in tables)
 foreach (var record in table.Records)
 { var v = record.GetValue(); if (v != null) res.Add(v.ToString()!); }
 return res.Distinct().OrderBy(s => s).ToList();
 }

 private bool HasRequiredInputs => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Token);
 private async Task ConnectAsync()
 {
 if (!HasRequiredInputs) { IsConnected = false; ConnectionStatus = "Fail"; DisplayStatus = "Fail: host and token are required"; return; }
 try
 {
 ConnectionStatus = "Connecting"; DisplayStatus = "Connecting";
 using var http = new HttpClient(); var baseUri = BuildBaseUri(Host, Port); var health = new Uri(baseUri, "health");
 if (!string.IsNullOrWhiteSpace(Token)) http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", Token);
 using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
 var resp = await http.GetAsync(health, cts.Token);
 if (!resp.IsSuccessStatusCode)
 {
 IsConnected = false; ConnectionStatus = "Fail"; DisplayStatus = $"Health fail {(int)resp.StatusCode}"; return;
 }
 // Additional auth validation: check bucket exists
 try
 {
 using var influx = new InfluxDBClient(baseUri.ToString(), Token);
 var bucket = await influx.GetBucketsApi().FindBucketByNameAsync(Bucket);
 if (bucket == null) { IsConnected = false; ConnectionStatus = "Fail"; DisplayStatus = "Bucket not found or no permission"; return; }
 // simple query to confirm org access
 await influx.GetQueryApi().QueryAsync("buckets() |> limit(n:1)", Org);
 }
 catch (Exception exAuth)
 {
 IsConnected = false; ConnectionStatus = "Fail"; DisplayStatus = $"Auth fail: {exAuth.Message}"; return;
 }
 IsConnected = true; ConnectionStatus = "Connected"; DisplayStatus = "Connected";
 }
 catch (Exception ex) { IsConnected = false; ConnectionStatus = "Fail"; DisplayStatus = $"Fail: {ex.Message}"; }
 }

 private static Uri BuildBaseUri(string host, string port)
 { if (!host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) host = "http://" + host.Trim(); var b = new UriBuilder(host); if (int.TryParse(port, out var p) && p >0) b.Port = p; else if (b.Port <=0) b.Port =8086; return b.Uri; }

 private void RaiseCanExec()
 { (ConnectCommand as IRelayCommand)?.RaiseCanExecuteChanged(); (NewQueryCommand as IRelayCommand)?.RaiseCanExecuteChanged(); (OpenTemplateCommand as IRelayCommand)?.RaiseCanExecuteChanged(); (SubmitQueryCommand as IRelayCommand)?.RaiseCanExecuteChanged(); (AddFilterPanelCommand as IRelayCommand)?.RaiseCanExecuteChanged(); (RemoveFilterPanelCommand as IRelayCommand)?.RaiseCanExecuteChanged(); (ExportCsvCommand as IRelayCommand)?.RaiseCanExecuteChanged(); (ExportExcelCommand as IRelayCommand)?.RaiseCanExecuteChanged(); }

 private async Task SubmitQueryAsync()
 {
 if (!IsConnected || string.IsNullOrWhiteSpace(Bucket)) return;
 try
 {
 IsQueryBusy = true; DisplayStatus = "Querying...";
 using var influx = new InfluxDBClient(BuildBaseUri(Host, Port).ToString(), Token);
 var queryApi = influx.GetQueryApi();
 var flux = BuildFluxFromSelections();
 var tables = await queryApi.QueryAsync(flux, Org);
 var table = new DataTable();
 var exclude = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { "result", "table", "_start", "_stop" };
 foreach (var t in tables)
 foreach (var rec in t.Records)
 {
 foreach (var kv in rec.Values)
 { if (exclude.Contains(kv.Key)) continue; if (!table.Columns.Contains(kv.Key)) table.Columns.Add(kv.Key, typeof(object)); }
 var row = table.NewRow();
 foreach (var kv in rec.Values)
 { if (exclude.Contains(kv.Key)) continue; row[kv.Key] = kv.Value ?? DBNull.Value; }
 table.Rows.Add(row);
 }
 PreviewTable = table; DisplayStatus = $"Query returned {table.Rows.Count} rows";
 }
 catch (Exception ex) { DisplayStatus = $"Query fail: {ex.Message}"; }
 finally { IsQueryBusy = false; RaiseCanExec(); }
 }

 private string BuildFluxFromSelections()
 {
 var sb = new StringBuilder(); sb.Append($"from(bucket: \"{Bucket}\")\n"); var (start, stop) = BuildRange(); sb.Append($"|> range(start: {start}, stop: {stop})\n");
 var meas = Filters.FirstOrDefault(f => f.Name == "_measurement")?.Values.Where(v => v.IsSelected).Select(v => v.Value).ToList() ?? new();
 var fields = Filters.FirstOrDefault(f => f.Name == "_field")?.Values.Where(v => v.IsSelected).Select(v => v.Value).ToList() ?? new();
 var tagFilters = Filters.Where(f => f.Name != "_measurement" && f.Name != "_field" && !f.Name.StartsWith("load_error")).Select(f => new { f.Name, Selected = f.Values.Where(v => v.IsSelected).Select(v => v.Value).ToList() }).Where(x => x.Selected.Count >0).ToList();
 if (meas.Count >0) sb.Append($"|> filter(fn: (r) => {string.Join(" or ", meas.Select(m => $"r._measurement == \"{EscapeFlux(m)}\""))})\n");
 if (fields.Count >0) sb.Append($"|> filter(fn: (r) => {string.Join(" or ", fields.Select(m => $"r._field == \"{EscapeFlux(m)}\""))})\n");
 foreach (var tg in tagFilters) sb.Append($"|> filter(fn: (r) => {string.Join(" or ", tg.Selected.Select(v => $"r[\"{EscapeFlux(tg.Name)}\"] == \"{EscapeFlux(v)}\""))})\n");
 return sb.ToString();
 }
 private static string EscapeFlux(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
 private (string start, string stop) BuildRange()
 {
 // Prefer DateTimePicker if set; fallback to legacy date+time fields
 try
 {
 if (FromDateTime.HasValue || ToDateTime.HasValue)
 {
 var from = FromDateTime ?? DateTime.UtcNow.AddHours(-1);
 var to = ToDateTime ?? DateTime.UtcNow;
 return (from.ToUniversalTime().ToString("o"), to.ToUniversalTime().ToString("o"));
 }
 var f = FromDate ?? DateTime.UtcNow.AddHours(-1);
 var t = ToDate ?? DateTime.UtcNow;
 if (TimeSpan.TryParse(FromTime, out var ft)) f = new DateTime(f.Year, f.Month, f.Day, ft.Hours, ft.Minutes, ft.Seconds, DateTimeKind.Utc);
 if (TimeSpan.TryParse(ToTime, out var tt)) t = new DateTime(t.Year, t.Month, t.Day, tt.Hours, tt.Minutes, tt.Seconds, DateTimeKind.Utc);
 return (f.ToUniversalTime().ToString("o"), t.ToUniversalTime().ToString("o"));
 }
 catch { var e = DateTime.UtcNow; return (e.AddHours(-1).ToString("o"), e.ToString("o")); }
 }

 private void ExportCsv()
 {
 if (PreviewTable == null || PreviewTable.Rows.Count ==0) return;
 var sfd = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"export_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
 if (sfd.ShowDialog() != true) return;
 var sep = ",";
 var sb = new StringBuilder();
 // header
 for (int i =0; i < PreviewTable.Columns.Count; i++)
 {
 if (i >0) sb.Append(sep);
 sb.Append(EscapeCsv(PreviewTable.Columns[i].ColumnName));
 }
 sb.AppendLine();
 // rows
 foreach (DataRow row in PreviewTable.Rows)
 {
 for (int i =0; i < PreviewTable.Columns.Count; i++)
 {
 if (i >0) sb.Append(sep);
 var val = row[i]?.ToString() ?? string.Empty;
 sb.Append(EscapeCsv(val));
 }
 sb.AppendLine();
 }
 System.IO.File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(true));
 }
 private static string EscapeCsv(string s)
 { bool needQuote = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'); if (needQuote) return '"' + s.Replace("\"", "\"\"") + '"'; return s; }

 private void ExportExcel()
 {
 if (PreviewTable == null || PreviewTable.Rows.Count ==0) return;
 var sfd = new SaveFileDialog { Filter = "Excel files (*.xlsx)|*.xlsx", FileName = $"export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx" };
 if (sfd.ShowDialog() != true) return;
 using var wb = new XLWorkbook();
 var ws = wb.Worksheets.Add(PreviewTable, "Data");
 ws.Columns().AdjustToContents();
 wb.SaveAs(sfd.FileName);
 }

 public event PropertyChangedEventHandler? PropertyChanged; protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? name = null) { if (Equals(storage, value)) return false; storage = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); return true; }
 }

 // Models
 public class QueryFilter : INotifyPropertyChanged { public string Name { get; set; } = string.Empty; public ObservableCollection<QueryFilterValue> Values { get; } = new(); public event PropertyChangedEventHandler? PropertyChanged; protected void OnPropertyChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p)); }
 public class QueryFilterValue : INotifyPropertyChanged { private bool _isSelected; public string Value { get; set; } = string.Empty; public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } } } public event PropertyChangedEventHandler? PropertyChanged; }

 // Panel VM used in QueryBuilder UI
 public class FilterPanel : INotifyPropertyChanged
 { private readonly MainViewModel _root; private string _selectedName = string.Empty; private string _searchText = string.Empty; public FilterPanel(MainViewModel root) { _root = root; }
 public string SelectedName { get => _selectedName; set { if (_selectedName != value) { _selectedName = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(ValuesView)); } } }
 public string SearchText { get => _searchText; set { if (_searchText != value) { _searchText = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(ValuesView)); } } }
 public QueryFilter? SelectedFilter => _root.Filters.FirstOrDefault(f => string.Equals(f.Name, SelectedName, StringComparison.OrdinalIgnoreCase));
 public System.Collections.Generic.IEnumerable<QueryFilterValue> ValuesView { get { var src = SelectedFilter?.Values ?? new ObservableCollection<QueryFilterValue>(); if (string.IsNullOrWhiteSpace(SearchText)) return src; var q = SearchText.Trim(); return src.Where(v => v.Value?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >=0); } }
 public event PropertyChangedEventHandler? PropertyChanged; protected void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); }

 public interface IRelayCommand { void RaiseCanExecuteChanged(); }
 public class SimpleRelayCommand : ICommand, IRelayCommand { private readonly Action _execute; private readonly Func<bool>? _can; public SimpleRelayCommand(Action e, Func<bool>? c = null) { _execute = e ?? throw new ArgumentNullException(nameof(e)); _can = c; } public bool CanExecute(object? p) => _can?.Invoke() ?? true; public void Execute(object? p) => _execute(); public event EventHandler? CanExecuteChanged; public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
 public class AsyncRelayCommand : ICommand, IRelayCommand { private readonly Func<Task> _exec; private readonly Func<bool>? _can; private bool _busy; public AsyncRelayCommand(Func<Task> e, Func<bool>? c = null) { _exec = e ?? throw new ArgumentNullException(nameof(e)); _can = c; } public bool CanExecute(object? p) => !_busy && (_can?.Invoke() ?? true); public async void Execute(object? p) { if (!CanExecute(p)) return; try { _busy = true; RaiseCanExecuteChanged(); await _exec(); } finally { _busy = false; RaiseCanExecuteChanged(); } } public event EventHandler? CanExecuteChanged; public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
 public class RelayCommand<T> : ICommand, IRelayCommand { private readonly Action<T?> _execute; private readonly Func<T?, bool>? _can; public RelayCommand(Action<T?> exec, Func<T?, bool>? can = null) { _execute = exec ?? throw new ArgumentNullException(nameof(exec)); _can = can; } public bool CanExecute(object? parameter) => _can?.Invoke((T?)parameter) ?? true; public void Execute(object? parameter) => _execute((T?)parameter); public event EventHandler? CanExecuteChanged; public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
}
