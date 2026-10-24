using System.IO;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using AlTamam.Contracts.Bridge;
using AlTamam.Desktop.Bridge;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AlTamam.Desktop;

public sealed class MainWindow : Window
{
    private readonly DesktopRuntime _runtime;
    private readonly WebView2 _browser=new();
    private readonly WebView2BridgeDispatcher _dispatcher;
    private readonly CancellationTokenSource _lifetime=new();
    private bool _closed, _closing, _allowClose;
    private readonly HashSet<Task> _pendingRequests = new();
    public Task Initialization {get;private set;}=Task.CompletedTask;
    public MainWindow(DesktopRuntime runtime)
    {
        _runtime=runtime;_dispatcher=new(runtime);
        Title="التمام اليومي";Width=Math.Min(1280,SystemParameters.WorkArea.Width);Height=Math.Min(820,SystemParameters.WorkArea.Height);MinWidth=Math.Min(800,SystemParameters.WorkArea.Width);MinHeight=Math.Min(600,SystemParameters.WorkArea.Height);
        WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=new SolidColorBrush(Color.FromRgb(245,247,250));
        Content=_browser;
        Loaded+=(_,_)=>Initialization=InitializeAsync();
        Closing+=OnClosing;
        Closed+=(_,_)=>{_closed=true;_lifetime.Cancel();_browser.Dispose();_lifetime.Dispose();};
    }
    private async Task InitializeAsync()
    {
        try
        {
            var fixedRuntime=Path.Combine(AppContext.BaseDirectory,"WebView2Runtime");
            var environment=await CoreWebView2Environment.CreateAsync(Directory.Exists(fixedRuntime)?fixedRuntime:null,
                Path.Combine(_runtime.Root,"WebView2"),new CoreWebView2EnvironmentOptions(
                    "--force-renderer-accessibility --disable-background-networking --disable-component-update --disable-sync --disable-domain-reliability --disable-features=BackForwardCache"));
            await _browser.EnsureCoreWebView2Async(environment);
            var core=_browser.CoreWebView2;
            var settings=core.Settings;
            settings.AreDevToolsEnabled=false;settings.AreDefaultContextMenusEnabled=false;
            settings.AreHostObjectsAllowed=false;settings.IsPasswordAutosaveEnabled=false;settings.IsGeneralAutofillEnabled=false;
            core.SetVirtualHostNameToFolderMapping(LocalWebUiOrigin.Host,Path.Combine(AppContext.BaseDirectory,"WebUI"),CoreWebView2HostResourceAccessKind.Deny);
            core.NavigationStarting+=(_,e)=>{if(!WebView2BridgeDispatcher.IsTrustedOrigin(e.Uri))e.Cancel=true;};
            core.NewWindowRequested+=(_,e)=>e.Handled=true;
            core.DownloadStarting+=(_,e)=>e.Cancel=true;
            core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
            core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
            core.WebResourceRequested+=(_,e)=>
            {
                if(!WebView2BridgeDispatcher.IsTrustedOrigin(e.Request.Uri))
                    e.Response=environment.CreateWebResourceResponse(null,403,"Blocked","Content-Type: text/plain");
            };
            core.WebMessageReceived+=async(_,e)=>
            {
                if(_closed||_closing)return;
                try
                {
                    var source=e.Source;var json=e.WebMessageAsJson;var token=_lifetime.Token;
                    var request=Task.Run(()=>_dispatcher.DispatchAsync(source,json,token),token);
                    _pendingRequests.Add(request);
                    try
                    {
                        var response=await request;
                        if(!_closed&&!_closing)core.PostWebMessageAsJson(JsonSerializer.Serialize(response,WebView2BridgeDispatcher.JsonOptions));
                    }
                    finally { _pendingRequests.Remove(request); }
                }
                catch(OperationCanceledException){}
                catch(Exception){if(!_closed)ShowFailure("تعذر استقبال نتيجة الطلب بأمان.");}
            };
            core.ProcessFailed+=(_,_)=>ShowFailure("توقف مكوّن العرض. أغلق البرنامج وافتحه مجدداً؛ تبقى البيانات المحفوظة محفوظة.");
            core.NavigationCompleted+=(_,e)=>{if(!e.IsSuccess&&e.WebErrorStatus!=CoreWebView2WebErrorStatus.OperationCanceled&&!_lifetime.IsCancellationRequested)ShowFailure("تعذر تحميل واجهة البرنامج المحلية.");};
            core.Navigate(LocalWebUiOrigin.BaseUrl+"index.html");
        }
        catch(Exception){ShowFailure("تعذر تشغيل واجهة Windows. تحقق من توفر مكوّن Microsoft Edge WebView2 Runtime وملفات واجهة البرنامج المحلية.");throw;}
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if(_allowClose)return;
        e.Cancel=true;
        if(_closing)return;
        _closing=true;
        _lifetime.Cancel();
        _browser.IsEnabled=false;
        try { await Task.WhenAll(_pendingRequests.ToArray()); }
        catch(OperationCanceledException) { }
        finally
        {
            // Defer the second Close until the current Closing event has returned.
            _ = Dispatcher.BeginInvoke(new Action(()=>{_allowClose=true;Close();}));
        }
    }
    private void ShowFailure(string message)
    {
        if(_lifetime.IsCancellationRequested)return;
        _browser.IsEnabled=false;
        Dispatcher.BeginInvoke(new Action(()=>{if(!_closed)MessageBox.Show(this,message,"تعذر عرض البرنامج",MessageBoxButton.OK,MessageBoxImage.Error);}));
    }
}
