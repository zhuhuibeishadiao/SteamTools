using DynamicData;
using DynamicData.Binding;
using Newtonsoft.Json.Linq;
using ReactiveUI;
using System.Application.Models;
using System.Application.Repositories;
using System.Application.UI.Resx;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Xml;
using WinAuth;

namespace System.Application.Services
{
    public class AuthService : ReactiveObject
    {
        #region static members
        public static AuthService Current { get; } = new();
        #endregion

        public SourceCache<MyAuthenticator, int> Authenticators { get; }

        public AuthService()
        {
            Authenticators = new SourceCache<MyAuthenticator, int>(t => t.Id);
        }

        public async void Initialize(bool isSync = false)
        {
            var repository = DI.Get<IGameAccountPlatformAuthenticatorRepository>();
            var list = await repository.GetAllAsync();
            if (list.Any_Nullable())
            {
                Authenticators.AddOrUpdate(list.Select(s => new MyAuthenticator(s)));

                if (isSync)
                {
                    Task.Run(() =>
                    {
                        foreach (var item in Authenticators.Items)
                            item.Sync();
                        //ToastService.Current.Notify(AppResources.LocalAuth_RefreshAuthSuccess);
                    }).ForgetAndDispose();
                }
                //else
                //    ToastService.Current.Notify(AppResources.LocalAuth_RefreshAuthSuccess);
            }

            //Authenticators.CollectionChanged += (s, e) =>
            //{
            //    this.RaisePropertyChanged(nameof(IsAuthenticatorsEmpty));
            //};
        }


        /// <summary>
        /// WinAuth令牌导入
        /// </summary>
        public void ImportWinAuthenticators(string file)
        {
            StringBuilder lines = new StringBuilder();
            bool retry;
            do
            {
                retry = false;
                lines.Length = 0;
                // read a plain text file
                lines.Append(File.ReadAllText(file));
            } while (retry);

            int linenumber = 0;
            try
            {
                using var sr = new StringReader(lines.ToString());
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    linenumber++;

                    // ignore blank lines or comments
                    line = line.Trim();
                    if (line.Length == 0 || line.IndexOf("#") == 0)
                    {
                        continue;
                    }

                    // bug if there is a hash before ?
                    var hash = line.IndexOf("#");
                    var qm = line.IndexOf("?");
                    if (hash != -1 && hash < qm)
                    {
                        line = $"{line.Substring(0, hash)}%23{line.Substring(hash + 1)}";
                    }

                    // parse and validate URI
                    var uri = new Uri(line);

                    // we only support "otpauth"
                    if (uri.Scheme != "otpauth")
                    {
                        throw new ApplicationException("Import only supports otpauth://");
                    }
                    // we only support totp (not hotp)
                    if (uri.Host != "totp" && uri.Host != "hotp")
                    {
                        throw new ApplicationException("Import only supports otpauth://totp/ or otpauth://hotp/");
                    }

                    // get the label and optional issuer
                    string issuer = string.Empty;
                    string label = (string.IsNullOrEmpty(uri.LocalPath) == false ? uri.LocalPath.Substring(1) : string.Empty); // skip past initial /
                    int p = label.IndexOf(":");
                    if (p != -1)
                    {
                        issuer = label.Substring(0, p);
                        label = label.Substring(p + 1);
                    }
                    // + aren't decoded
                    label = label.Replace("+", " ");

                    var query = HttpUtility.ParseQueryString(uri.Query);
                    string secret = query["secret"];
                    if (string.IsNullOrEmpty(secret) == true)
                    {
                        throw new ApplicationException("Authenticator does not contain secret");
                    }

                    string counter = query["counter"];
                    if (uri.Host == "hotp" && string.IsNullOrEmpty(counter) == true)
                    {
                        throw new ApplicationException("HOTP authenticator should have a counter");
                    }

                    GAPAuthenticatorDTO importedAuthenticator = new();
                    //
                    GAPAuthenticatorValueDTO auth;
                    if (string.Compare(issuer, "BattleNet", true) == 0)
                    {
                        string serial = query["serial"];
                        if (string.IsNullOrEmpty(serial) == true)
                        {
                            throw new ApplicationException("Battle.net Authenticator does not have a serial");
                        }
                        serial = serial.ToUpper();
                        if (Regex.IsMatch(serial, @"^[A-Z]{2}-?[\d]{4}-?[\d]{4}-?[\d]{4}$") == false)
                        {
                            throw new ApplicationException("Invalid serial for Battle.net Authenticator");
                        }
                        auth = new GAPAuthenticatorValueDTO.BattleNetAuthenticator();
                        //char[] decoded = Base32.getInstance().Decode(secret).Select(c => Convert.ToChar(c)).ToArray(); // this is hex string values
                        //string hex = new string(decoded);
                        //((BattleNetAuthenticator)auth).SecretKey = Authenticator.StringToByteArray(hex);

                        ((GAPAuthenticatorValueDTO.BattleNetAuthenticator)auth).SecretKey = WinAuthBase32.GetInstance().Decode(secret);

                        ((GAPAuthenticatorValueDTO.BattleNetAuthenticator)auth).Serial = serial;

                        issuer = string.Empty;
                    }
                    else if (string.Compare(issuer, "Steam", true) == 0)
                    {
                        auth = new GAPAuthenticatorValueDTO.SteamAuthenticator();
                        ((GAPAuthenticatorValueDTO.SteamAuthenticator)auth).SecretKey = WinAuthBase32.GetInstance().Decode(secret);
                        ((GAPAuthenticatorValueDTO.SteamAuthenticator)auth).Serial = string.Empty;
                        ((GAPAuthenticatorValueDTO.SteamAuthenticator)auth).DeviceId = query["deviceid"] ?? string.Empty;
                        ((GAPAuthenticatorValueDTO.SteamAuthenticator)auth).SteamData = query["data"] ?? string.Empty;
                        issuer = string.Empty;
                    }
                    else if (uri.Host == "hotp")
                    {
                        auth = new GAPAuthenticatorValueDTO.HOTPAuthenticator();
                        ((GAPAuthenticatorValueDTO.HOTPAuthenticator)auth).SecretKey = WinAuthBase32.GetInstance().Decode(secret);
                        ((GAPAuthenticatorValueDTO.HOTPAuthenticator)auth).Counter = int.Parse(counter);

                        if (string.IsNullOrEmpty(issuer) == false)
                        {
                            auth.Issuer = issuer;
                        }
                    }
                    else // if (string.Compare(issuer, "Google", true) == 0)
                    {
                        auth = new GAPAuthenticatorValueDTO.GoogleAuthenticator();
                        ((GAPAuthenticatorValueDTO.GoogleAuthenticator)auth).Enroll(secret);

                        if (string.Compare(issuer, "Google", true) == 0)
                        {
                            issuer = string.Empty;
                        }
                        else if (string.IsNullOrEmpty(issuer) == false)
                        {
                            auth.Issuer = issuer;
                        }
                    }

                    int.TryParse(query["period"], out int period);
                    if (period != 0)
                    {
                        auth.Period = period;
                    }

                    int.TryParse(query["digits"], out int digits);
                    if (digits != 0)
                    {
                        auth.CodeDigits = digits;
                    }


                    if (Enum.TryParse<GAPAuthenticatorValueDTO.HMACTypes>(query["algorithm"], true, out GAPAuthenticatorValueDTO.HMACTypes hmactype) == true)
                    {
                        auth.HMACType = hmactype;
                    }

                    //
                    if (label.Length != 0)
                    {
                        importedAuthenticator.Name = (issuer.Length != 0 ? issuer + " (" + label + ")" : label);
                    }
                    else if (issuer.Length != 0)
                    {
                        importedAuthenticator.Name = issuer;
                    }
                    else
                    {
                        importedAuthenticator.Name = "Imported";
                    }
                    //
                    importedAuthenticator.Value = auth;

                    // sync
                    ToastService.Current.Notify(AppResources.LocalAuth_AddAuthSyncTip);
                    importedAuthenticator.Value.Sync();

                    AuthService.AddOrUpdateSaveAuthenticators(importedAuthenticator);
                }
                ToastService.Current.Notify(AppResources.LocalAuth_AddAuthSuccess);
            }
            catch (UriFormatException ex)
            {
                throw new UriFormatException(string.Format("Invalid authenticator at line {0}", linenumber), ex);
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("Error importing at line {0}", ex.Message), ex);
            }
        }

        /// <summary>
        /// Steam APP令牌导入
        /// </summary>
        /// <returns>true if successful</returns>
        public bool ImportSteamGuard(string name, string uuid, string steamGuard)
        {
            if (string.IsNullOrEmpty(uuid))
            {
                //WinAuthForm.ErrorDialog(this, "Please enter the contents of the steam.uuid.xml file or your DeviceId");
                return false;
            }
            if (steamGuard.Length == 0)
            {
                //WinAuthForm.ErrorDialog(this, "Please enter the contents of your SteamGuard file");
                return false;
            }

            // check the deviceid
            string deviceId;
            if (uuid.IndexOf("?xml") != -1)
            {
                try
                {
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(uuid);
                    var node = doc.SelectSingleNode("//string[@name='uuidKey']");
                    if (node == null)
                    {
                        //WinAuthForm.ErrorDialog(this, "Cannot find uuidKey in xml");
                        return false;
                    }

                    deviceId = node.InnerText;
                }
                catch (Exception ex)
                {
                    //WinAuthForm.ErrorDialog(this, "Invalid uuid xml: " + ex.Message);
                    //ToastService.Current.Notify("Invalid uuid xml");
                    Log.Error(nameof(AuthService), ex, nameof(ImportSteamGuard));
                    return false;
                }
            }
            else
            {
                deviceId = uuid;
            }
            if (string.IsNullOrEmpty(deviceId) || Regex.IsMatch(deviceId, @"android:[0-9abcdef-]+", RegexOptions.Singleline | RegexOptions.IgnoreCase) == false)
            {
                //WinAuthForm.ErrorDialog(this, "Invalid deviceid, expecting \"android:NNNN...\"");
                return false;
            }

            // check the steamguard
            byte[] secret;
            string serial;
            try
            {
                var json = JObject.Parse(steamGuard);

                var node = json.SelectToken("shared_secret");
                if (node == null)
                {
                    throw new ApplicationException("no shared_secret");
                }
                secret = Convert.FromBase64String(node.Value<string>());

                node = json.SelectToken("serial_number");
                if (node == null)
                {
                    throw new ApplicationException("no serial_number");
                }
                serial = node.Value<string>();
            }
            catch (Exception ex)
            {
                //WinAuthForm.ErrorDialog(this, "Invalid SteamGuard JSON contents: " + ex.Message);
                //ToastService.Current.Notify("Invalid SteamGuard JSON");
                Log.Error(nameof(AuthService), ex, nameof(ImportSteamGuard));
                return false;
            }

            GAPAuthenticatorValueDTO.SteamAuthenticator auth = new GAPAuthenticatorValueDTO.SteamAuthenticator
            {
                SecretKey = secret,
                Serial = serial,
                SteamData = steamGuard,
                DeviceId = deviceId
            };

            AuthService.AddOrUpdateSaveAuthenticators(new GAPAuthenticatorDTO
            {
                Name = name,
                Value = auth,
            });
            return true;
        }

        public bool ImportSDAFile(string mafile)
        {
            string data;
            if (File.Exists(mafile) == false || (data = File.ReadAllText(mafile)) == null)
            {
                throw new ApplicationException("Cannot read file " + mafile);
            }

            var token = JObject.Parse(data);
            var sdaentry = new ImportedSDAEntry
            {
                Username = token.SelectToken("account_name")?.Value<string>(),
            };
            if (string.IsNullOrEmpty(sdaentry.SteamId) == true)
            {
                sdaentry.SteamId = token.SelectToken("Session.SteamID")?.Value<string>();
            }
            if (string.IsNullOrEmpty(sdaentry.SteamId) == true)
            {
                sdaentry.SteamId = mafile.Split('.')[0];
            }
            sdaentry.json = data;

            //importSDAList.Items.Add(sdaentry);
            GAPAuthenticatorValueDTO.SteamAuthenticator auth = new();
            GAPAuthenticatorDTO winAuth = new();
            foreach (var prop in token.Root.Children().ToList())
            {
                var child = token.SelectToken(prop.Path);

                string lkey = prop.Path.ToLower();
                if (lkey == "fully_enrolled" || lkey == "session")
                {
                    prop.Remove();
                }
                else if (lkey == "device_id")
                {
                    auth.DeviceId = child.Value<string>();
                    prop.Remove();
                }
                else if (lkey == "serial_number")
                {
                    auth.Serial = child.Value<string>();
                }
                else if (lkey == "account_name")
                {
                    //if (this.nameField.Text.Length == 0)
                    //{
                    //    this.nameField.Text = "Steam (" + child.Value<string>() + ")";
                    //}
                    winAuth.Name = "Steam (" + child.Value<string>() + ")";
                }
                else if (lkey == "shared_secret")
                {
                    auth.SecretKey = Convert.FromBase64String(child.Value<string>());
                }
            }
            auth.SteamData = token.ToString(Newtonsoft.Json.Formatting.None);
            winAuth.Value = auth;

            AddOrUpdateSaveAuthenticators(winAuth);
            return true;
        }

        /// <summary>
        /// 导入Steam++导出的令牌数据文件 V2
        /// </summary>
        public void ImportAuthenticatorFile()
        {

        }

        public void LoadSteamToolsV1Authenticator(string file)
        {
            var authString = File.ReadAllText(file).DecompressString();
            if (!string.IsNullOrEmpty(authString))
            {
                XmlReader reader = XmlReader.Create(new StringReader(authString));
                reader.Read();
                while (reader.EOF == false && reader.IsEmptyElement == true)
                {
                    reader.Read();
                }
                reader.MoveToContent();
                while (reader.EOF == false)
                {
                    if (reader.IsStartElement())
                    {
                        if (reader.Name == "Auth")
                        {
                            reader.Read();
                        }
                        if (reader.Name == "WinAuthAuthenticator")
                        {
                            var wa = new MyAuthenticator();
                            wa.ReadXml(reader, null);
                            AddOrUpdateSaveAuthenticators(wa);
                        }
                    }
                    else
                    {
                        reader.Read();
                        break;
                    }
                }
            }
        }

        public static void AddOrUpdateSaveAuthenticators(GAPAuthenticatorDTO auth)
        {
            AddOrUpdateSaveAuthenticators(new MyAuthenticator(auth));
        }

        public static async void AddOrUpdateSaveAuthenticators(MyAuthenticator auth)
        {
            var repository = DI.Get<IGameAccountPlatformAuthenticatorRepository>();
            await repository.InsertOrUpdateAsync(auth.AuthenticatorData, true);
            if (Current.Authenticators.Items.Any(s => s.Id == auth.Id))
            {
                return;
            }
            Current.Authenticators.AddOrUpdate(auth);
        }

        public static async void DeleteSaveAuthenticators(MyAuthenticator auth)
        {
            var repository = DI.Get<IGameAccountPlatformAuthenticatorRepository>();
            if (auth.AuthenticatorData.ServerId.HasValue)
            {
                await repository.DeleteAsync(auth.AuthenticatorData.ServerId.Value);
            }
            await repository.DeleteAsync(auth.AuthenticatorData.Id);
            Current.Authenticators.Remove(auth);
        }


        public void SaveEditNameAuthenticators()
        {
            var auths = Authenticators.Items.Where(x => x.Name != x.OriginName);

            foreach (var auth in auths)
                AddOrUpdateSaveAuthenticators(auth);
        }
    }
}
