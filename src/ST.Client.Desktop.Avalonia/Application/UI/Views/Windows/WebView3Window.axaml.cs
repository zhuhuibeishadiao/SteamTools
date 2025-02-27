using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CefNet;
using CefNet.JSInterop;
using CefSharp;
using ReactiveUI;
using System.Application.UI.Resx;
using System.Application.UI.ViewModels;
using System.Application.UI.Views.Controls;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static System.Application.Services.CloudService.Constants;
using static System.Application.Services.ISteamService;

namespace System.Application.UI.Views.Windows
{
    public class WebView3Window : FluentWindow, IDisposable
    {
        readonly WebView3 webView;
        bool disposedValue;

        public WebView3Window() : base()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            webView = this.FindControl<WebView3>(nameof(webView));
            webView.InitialUrl = WebView3WindowViewModel.AboutBlank;
            webView.Opacity = 0;
            webView.DocumentTitleChanged += WebView_DocumentTitleChanged;
            webView.LoadingStateChange += WebView_LoadingStateChange;
        }

        CancellationTokenSource? cts;

        /// <summary>
        /// 第一次 WebView 加载超时检查
        /// </summary>
        /// <param name="vm"></param>
        async void FirstWebViewLoadingTimeoutInspect(WebView3WindowViewModel vm)
        {
            if (cts == null)
            {
                cts = new();
                var isDelayed = false;
                try
                {
                    await Task.Delay(vm.Timeout, cts.Token);
                    isDelayed = true;
                }
                catch (OperationCanceledException)
                {
                }
                if (isDelayed && vm.IsLoading)
                {
                    webView.Stop();
                    Hide();
                    await MessageBoxCompat.ShowAsync(
                        vm.TimeoutErrorMessage!,
                        vm.Title,
                        MessageBoxButtonCompat.OKCancel);
                    Close();
                }
            }
        }

        bool isFirstWebViewLoading;
        //#if DEBUG
        //        bool isShowDevTools;
        //#endif
        private async void WebView_LoadingStateChange(object? sender, LoadingStateChangeEventArgs e)
        {
            //#if DEBUG
            //            if (!isShowDevTools)
            //            {
            //                webView.ShowDevTools();
            //                isShowDevTools = true;
            //            }
            //#endif
            if (DataContext is WebView3WindowViewModel vm)
            {
                if (!isFirstWebViewLoading)
                {
                    isFirstWebViewLoading = true;
                    if (!string.IsNullOrWhiteSpace(vm.TimeoutErrorMessage))
                    {
                        FirstWebViewLoadingTimeoutInspect(vm);
                    }
                }
                if (!e.Busy)
                {
                    if (loginUsingSteamClientState == LoginUsingSteamClientState.Loading)
                    {
                        var frame = webView.GetMainFrame();
                        if (frame == null)
                        {
                            LoginUsingSteamClientFinishNavigate(vm);
                        }
                        try
                        {
                            dynamic scriptable = await frame.GetScriptableObjectAsync(CancellationToken.None).ConfigureAwait(true);
                            scriptable.window.eval("function V_SetCookie() {} function V_GetCookie() {}");
                            scriptable.window.LoginUsingSteamClient("https://steamcommunity.com");
                            loginUsingSteamClientState = LoginUsingSteamClientState.Success;
                        }
                        catch (Exception ex)
                        {
#if DEBUG
                            Log.Error(nameof(WebView3Window), ex, "JSInterop fail.");
#endif
                            LoginUsingSteamClientFinishNavigate(vm);
                        }
                    }
                    else if (loginUsingSteamClientState == LoginUsingSteamClientState.Success)
                    {
                        LoginUsingSteamClientFinishNavigate(vm);
                    }
                    else
                    {
                        if (webView.Opacity != 1) webView.Opacity = 1;
                        if (vm.IsLoading)
                        {
                            vm.IsLoading = false;
                        }
                    }
                }
            }
        }

        void Navigate(string url)
        {
            if (webView.BrowserObject == null)
            {
                webView.InitialUrl = url;
            }
            else
            {
                webView.Navigate(url);
            }
        }

        void LoginUsingSteamClientFinishNavigate(WebView3WindowViewModel? vm = null)
        {
            loginUsingSteamClientState = LoginUsingSteamClientState.None;
            if (vm == null && DataContext is WebView3WindowViewModel vm2) vm = vm2;
            Navigate(vm?.Url ?? WebView3WindowViewModel.AboutBlank);
        }

        static Task? mGetLoginUsingSteamClientCookiesAsync;
        static async Task GetLoginUsingSteamClientCookiesAsync()
        {
            var cookies = await Instance.GetLoginUsingSteamClientCookieCollectionAsync(runasInvoker: DI.Platform == Platform.Windows);
            if (cookies != default)
            {
                var manager = CefRequestContext.GetGlobalContext().GetCookieManager(null);
                foreach (Cookie item in cookies)
                {
                    var cookie = item.GetCefNetCookie();
                    var setCookieResult = await manager.SetCookieAsync(url_steamcommunity_checkclientautologin, cookie);
                    if (item.Name == "steamLoginSecure" && !setCookieResult)
                    {
                        return;
                    }
                }
            }
        }

        async void GetLoginUsingSteamClientCookies()
        {
            if (mGetLoginUsingSteamClientCookiesAsync == null)
                mGetLoginUsingSteamClientCookiesAsync = GetLoginUsingSteamClientCookiesAsync();
            await mGetLoginUsingSteamClientCookiesAsync;
            LoginUsingSteamClientFinishNavigate();
        }

        LoginUsingSteamClientState loginUsingSteamClientState;
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is WebView3WindowViewModel vm)
            {
                vm.Close += Close;
                if (!string.IsNullOrWhiteSpace(vm.Title))
                    webView.DocumentTitleChanged -= WebView_DocumentTitleChanged;
                if (vm.UseLoginUsingSteamClient)
                {
                    loginUsingSteamClientState = LoginUsingSteamClientState.Loading;
                    Navigate("https://steamcommunity.com/login/home/?goto=my/profile");
                }
                else if (vm.UseLoginUsingSteamClientV2)
                {
                    vm.Timeout += TimeSpan.FromMilliseconds(IPC_Call_GetLoginUsingSteamClient_Timeout_MS);
                    loginUsingSteamClientState = LoginUsingSteamClientState.Loading;
                    GetLoginUsingSteamClientCookies();
                }
                vm.WhenAnyValue(x => x.Url).WhereNotNull().Subscribe(x =>
                {
                    if (loginUsingSteamClientState == default)
                    {
                        if (x == WebView3WindowViewModel.AboutBlank)
                        {
                            webView.Opacity = 0;
                        }
                        else if (IsHttpUrl(x))
                        {
                            if (x.StartsWith("https://steampp.net", StringComparison.OrdinalIgnoreCase))
                                x = string.Format(
                                    x + "?theme={0}&language={1}",
                                    CefNetApp.GetTheme(),
                                    R.Language);
                            //if (webView.Opacity != 1) webView.Opacity = 1;
                            Navigate(x);
                        }
                    }
                }).AddTo(vm);
                vm.WhenAnyValue(x => x.StreamResponseFilterUrls).Subscribe(x => webView.StreamResponseFilterUrls = x).AddTo(vm);
                vm.WhenAnyValue(x => x.FixedSinglePage).Subscribe(x => webView.FixedSinglePage = x).AddTo(vm);
                vm.WhenAnyValue(x => x.IsSecurity).Subscribe(x => webView.IsSecurity = x).AddTo(vm);
                webView.OnStreamResponseFilterResourceLoadComplete += vm.OnStreamResponseFilterResourceLoadComplete;
            }
        }

        void WebView_DocumentTitleChanged(object? sender, DocumentTitleChangedEventArgs e)
        {
            if (DataContext is WindowViewModel vm)
            {
                vm.Title = e.Title;
            }
            else
            {
                Title = e.Title;
            }
        }

        void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Dispose();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: 释放托管状态(托管对象)
                    if (webView != null)
                    {
                        cts?.Cancel();
                        webView.DocumentTitleChanged -= WebView_DocumentTitleChanged;
                        webView.LoadingStateChange -= WebView_LoadingStateChange;
                        if (DataContext is WebView3WindowViewModel vm)
                        {
                            webView.OnStreamResponseFilterResourceLoadComplete -= vm.OnStreamResponseFilterResourceLoadComplete;
                        }
                        ((IDisposable)webView).Dispose();
                    }
                    if (DataContext is IDisposable d)
                    {
                        d.Dispose();
                    }
                }

                // TODO: 释放未托管的资源(未托管的对象)并替代终结器
                // TODO: 将大型字段设置为 null
                disposedValue = true;
            }
        }

        // // TODO: 仅当“Dispose(bool disposing)”拥有用于释放未托管资源的代码时才替代终结器
        // ~WebView3Window()
        // {
        //     // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // 不要更改此代码。请将清理代码放入“Dispose(bool disposing)”方法中
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        enum LoginUsingSteamClientState
        {
            None,
            Loading,
            Success,
        }
    }
}