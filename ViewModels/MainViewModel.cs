using System;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace influx2Exporter.ViewModels
{
 public class MainViewModel : INotifyPropertyChanged
 {
 private string _host = string.Empty;
 private string _org = string.Empty;
 private string _token = string.Empty;
 private string _bucket = string.Empty;
 private string _port = string.Empty;
 private string _connectionStatus = "Disconnected"; // semantic state for styling
 private string _displayStatus = "Disconnected"; // text shown in UI (can include error details)
 private bool _isConnected;

 public string Host { get => _host; set { if (SetProperty(ref _host, value)) RaiseCanExec(); } }
 public string Org { get => _org; set { if (SetProperty(ref _org, value)) RaiseCanExec(); } }
 public string Token { get => _token; set { if (SetProperty(ref _token, value)) RaiseCanExec(); } }
 public string Bucket { get => _bucket; set { if (SetProperty(ref _bucket, value)) RaiseCanExec(); } }
 public string Port { get => _port; set { if (SetProperty(ref _port, value)) RaiseCanExec(); } }

 public string ConnectionStatus { get => _connectionStatus; set { if (SetProperty(ref _connectionStatus, value)) { /* keep */ } } }
 public string DisplayStatus { get => _displayStatus; set => SetProperty(ref _displayStatus, value); }
 public bool IsConnected { get => _isConnected; set { if (SetProperty(ref _isConnected, value)) RaiseCanExec(); } }

 public ICommand ConnectCommand { get; }
 public ICommand NewQueryCommand { get; }
 public ICommand OpenTemplateCommand { get; }

 public MainViewModel()
 {
 // Enable Connect whenever not connected; validation is handled inside ConnectAsync to show Fail reasons
 ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsConnected);
 NewQueryCommand = new SimpleRelayCommand(() => { /* open new query UI */ }, () => IsConnected);
 OpenTemplateCommand = new SimpleRelayCommand(() => { /* open template UI */ }, () => IsConnected);
 }

 private bool HasRequiredInputs =>
 !string.IsNullOrWhiteSpace(Host) &&
 !string.IsNullOrWhiteSpace(Token); // Org/Bucket optional for connectivity check

 private async Task ConnectAsync()
 {
 if (!HasRequiredInputs)
 {
 IsConnected = false;
 ConnectionStatus = "Fail"; // for styling (red)
 DisplayStatus = "Fail: host and token are required";
 return;
 }

 try
 {
 ConnectionStatus = "Connecting"; // orange
 DisplayStatus = "Connecting";
 using var http = new HttpClient();
 var baseUri = BuildBaseUri(Host, Port);
 var healthUri = new Uri(baseUri, "health");
 if (!string.IsNullOrWhiteSpace(Token))
 {
 http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", Token);
 }
 using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
 var resp = await http.GetAsync(healthUri, cts.Token); // capture context for UI updates
 if (resp.IsSuccessStatusCode)
 {
 IsConnected = true;
 ConnectionStatus = "Connected"; // green
 DisplayStatus = "Connected";
 }
 else
 {
 IsConnected = false;
 ConnectionStatus = "Fail"; // red
 var reason = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
 string? body = null;
 try { body = await resp.Content.ReadAsStringAsync(); } catch { }
 if (!string.IsNullOrWhiteSpace(body))
 {
 body = body.Length >120 ? body.Substring(0,120) + "..." : body;
 reason += $" - {body}";
 }
 DisplayStatus = $"Fail: {reason}";
 }
 }
 catch (Exception ex)
 {
 IsConnected = false;
 ConnectionStatus = "Fail";
 DisplayStatus = $"Fail: {ex.Message}";
 }
 }

 private static Uri BuildBaseUri(string host, string port)
 {
 if (!host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
 !host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
 {
 host = "http://" + host.Trim();
 }
 var builder = new UriBuilder(host);
 if (int.TryParse(port, out var p) && p >0)
 {
 builder.Port = p;
 }
 else if (builder.Port <=0)
 {
 builder.Port =8086; // default InfluxDB port
 }
 return builder.Uri;
 }

 private void RaiseCanExec()
 {
 (ConnectCommand as IRelayCommand)?.RaiseCanExecuteChanged();
 (NewQueryCommand as IRelayCommand)?.RaiseCanExecuteChanged();
 (OpenTemplateCommand as IRelayCommand)?.RaiseCanExecuteChanged();
 }

 public event PropertyChangedEventHandler? PropertyChanged;
 protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
 {
 if (Equals(storage, value)) return false;
 storage = value;
 PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
 return true;
 }
 }

 // Interface to raise CanExecuteChanged
 public interface IRelayCommand
 {
 void RaiseCanExecuteChanged();
 }

 // Minimal ICommand implementation (sync)
 public class SimpleRelayCommand : ICommand, IRelayCommand
 {
 private readonly Action _execute;
 private readonly Func<bool>? _canExecute;
 public SimpleRelayCommand(Action execute, Func<bool>? canExecute = null)
 {
 _execute = execute ?? throw new ArgumentNullException(nameof(execute));
 _canExecute = canExecute;
 }
 public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
 public void Execute(object? parameter) => _execute();
 public event EventHandler? CanExecuteChanged;
 public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
 }

 // Async variant for awaitable actions
 public class AsyncRelayCommand : ICommand, IRelayCommand
 {
 private readonly Func<Task> _executeAsync;
 private readonly Func<bool>? _canExecute;
 private bool _isExecuting;
 public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
 {
 _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
 _canExecute = canExecute;
 }
 public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);
 public async void Execute(object? parameter)
 {
 if (!CanExecute(parameter)) return;
 try
 {
 _isExecuting = true; RaiseCanExecuteChanged();
 await _executeAsync();
 }
 finally
 {
 _isExecuting = false; RaiseCanExecuteChanged();
 }
 }
 public event EventHandler? CanExecuteChanged;
 public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
 }
}
