using ReactiveUI;
using System.Application.Models;
using System.Application.Services;
using System.Application.UI.Resx;
using System.IO;
using System.Net;
using System.Properties;
using System.Threading.Tasks;
using System.Windows;

namespace System.Application.UI.ViewModels
{
    public class AddAuthWindowViewModel : WindowViewModel
    {
        readonly IHttpService httpService = DI.Get<IHttpService>();

        public AddAuthWindowViewModel() : base()
        {
            Title = ThisAssembly.AssemblyTrademark + " | " + AppResources.LocalAuth_AddAuth;
        }

        private GAPAuthenticatorValueDTO.SteamAuthenticator _SteamAuthenticator;
        private GAPAuthenticatorValueDTO.SteamAuthenticator.EnrollState _Enroll = new() { RequiresLogin = true };

        public string AuthName { get; set; }

        public string UUID { get; set; }

        public string SteamGuard { get; set; }

        public bool RequiresLogin
        {
            get
            {
                return _Enroll.RequiresLogin;
            }
            set
            {
                _Enroll.RequiresLogin = value;
                this.RaisePropertyChanged();
            }
        }

        public bool RequiresActivation
        {
            get
            {
                return _Enroll.RequiresActivation;
            }
            set
            {
                this.RaisePropertyChanged();
            }
        }

        public string UserName
        {
            get
            {
                return _Enroll.Username;
            }
            set
            {
                _Enroll.Username = value;
            }
        }

        public string Password
        {
            get
            {
                return _Enroll.Password;
            }
            set
            {
                _Enroll.Password = value;
            }
        }

        public string? CaptchaText
        {
            get
            {
                return _Enroll.CaptchaText;
            }
            set
            {
                _Enroll.CaptchaText = value;
                this.RaisePropertyChanged();
            }
        }

        public string? EmailAuthText
        {
            get
            {
                return _Enroll.EmailAuthText;
            }
            set
            {
                _Enroll.EmailAuthText = value;
            }
        }

        public string? ActivationCode
        {
            get
            {
                return _Enroll.ActivationCode;
            }
            set
            {
                _Enroll.ActivationCode = value;
            }
        }

        public string? RevocationCode
        {
            get
            {
                return _Enroll.RevocationCode;
            }
            set
            {
                this.RaisePropertyChanged();
            }
        }

        private Stream? _CaptchaImage;
        public Stream? CaptchaImage
        {
            get => _CaptchaImage;
            set => this.RaiseAndSetIfChanged(ref _CaptchaImage, value);
        }

        private string? _EmailDomain;
        public string? EmailDomain
        {
            get => _EmailDomain;
            set => this.RaiseAndSetIfChanged(ref _EmailDomain, value);
        }

        private bool _RequiresAdd;
        public bool RequiresAdd
        {
            get => _RequiresAdd;
            set => this.RaiseAndSetIfChanged(ref _RequiresAdd, value);
        }

        public void ImportSteamGuard()
        {
            if (AuthService.Current.ImportSteamGuard(AuthName, UUID, SteamGuard))
            {
                ToastService.Current.Notify(AppResources.LocalAuth_AddAuthSuccess);
            }
            else
            {
                ToastService.Current.Notify(AppResources.LocalAuth_ImportFaild);
            }
        }

        private bool _IsLogining = false;
        public void LoginSteamImport()
        {
            if (_IsLogining)
            {
                return;
            }
            _IsLogining = true;

            if (_SteamAuthenticator == null)
                _SteamAuthenticator = new GAPAuthenticatorValueDTO.SteamAuthenticator();

            _Enroll.Language = R.GetCurrentCultureSteamLanguageName();

            bool result = false;
            Task.Run(() =>
            {
                ToastService.Current.Set(AppResources.Logining);
                result = _SteamAuthenticator.Enroll(_Enroll);
                ToastService.Current.Set();

                if (result == false)
                {
                    if (string.IsNullOrEmpty(_Enroll.Error) == false)
                    {
                        MessageBoxCompat.Show(_Enroll.Error);
                        //ToastService.Current.Notify(_Enroll.Error);
                    }

                    if (_Enroll.Requires2FA == true)
                    {
                        MessageBoxCompat.Show(AppResources.LocalAuth_SteamUser_Requires2FA);
                        //ToastService.Current.Notify(AppResources.LocalAuth_SteamUser_Requires2FA);
                        return;
                    }

                    if (_Enroll.RequiresCaptcha == true)
                    {
                        CaptchaText = null;
                        CaptchaImage = null;
                        using var web = new WebClient();
                        var bt = web.DownloadData(_Enroll.CaptchaUrl);
                        using var stream = new MemoryStream(bt);
                        CaptchaImage = stream;
                        return;
                    }

                    if (_Enroll.RequiresEmailAuth == true)
                    {
                        RequiresLogin = false;
                        CaptchaText = null;
                        CaptchaImage = null;
                        EmailDomain = string.IsNullOrEmpty(_Enroll.EmailDomain) == false ? "***@" + _Enroll.EmailDomain : string.Empty;
                        return;
                    }

                    if (_Enroll.RequiresActivation == true)
                    {
                        EmailDomain = null;
                        _Enroll.Error = null;
                        RequiresLogin = false;

                        AuthService.AddOrUpdateSaveAuthenticators(new GAPAuthenticatorDTO
                        {
                            Name = nameof(GamePlatform.Steam) + "(" + UserName + ")",
                            Value = _SteamAuthenticator
                        });

                        RequiresActivation = true;
                        RevocationCode = _Enroll.RevocationCode;
                        return;
                    }

                    if (_Enroll.RequiresLogin == true)
                    {
                        return;
                    }

                    string error = _Enroll.Error;
                    if (string.IsNullOrEmpty(error) == true)
                    {
                        error = AppResources.LocalAuth_SteamUser_Error;
                    }
                    MessageBoxCompat.Show(_Enroll.Error);
                    return;
                }
                RequiresActivation = false;
                RequiresAdd = true;
            }).ContinueWith(s =>
            {
                Log.Error(nameof(AddAuthWindowViewModel), s.Exception, nameof(LoginSteamImport));
                MessageBoxCompat.Show(s.Exception);
                //ToastService.Current.Notify(s.Exception.Message);
            }, TaskContinuationOptions.OnlyOnFaulted).ContinueWith(s =>
            {
                _IsLogining = false;
                ToastService.Current.Set();
                s.Dispose();
            });

        }

    }
}