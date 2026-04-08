using Avalonia.Controls; // Added for Avalonia UI controls like TextBox
using Avalonia.Threading;
using DexInstructionRunner.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace DexInstructionRunner.Services
{
    public class AuthenticationResult
    {
        public string Token { get; set; }
        public string PrincipalName { get; set; }
    }

    public class AuthenticationService
    {
        private string _token;
        private string _platformUrl;
        private Timer _authCheckTimer;
        private int _reauthThresholdMinutes = 5;
        private readonly TextBox _logTextBox;
        private DateTime? _expirationTime;
        private bool _enableDevTokenReuse;
        public event Action<string>? TokenTimeRemainingUpdated;
        private readonly string _tokenFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DexConsole");
        private readonly string _tokenFileName = "tvappdata.bin";
        private bool _tokenRefreshInProgress = false;
        private readonly ExperienceService _experienceService;
        private readonly TokenStoreService _tokenStoreService;  // Added field for TokenStoreService
        private MainWindow _mainWindow;
        private string TokenFilePath => Path.Combine(_tokenFolderPath, _tokenFileName);

        // Prefer deterministic tenant-mode detection by probing platform info endpoints.
        // Multi-tenant:   https://{platform}/api/information
        // Single-tenant:  https://{platform}/tachyon/api/information
        // If probing fails, we fall back to the original auth attempt order.

        // Constructor: Initialize with config helper, log text box, experience service, and token store service
        private readonly string _consumerName;
        public AuthenticationService(ConfigHelper configHelper, TextBox logTextBox, ExperienceService experienceService, TokenStoreService tokenStoreService, string consumerName, MainWindow mainWindow)
        {
            var config = configHelper.GetConfiguration();
            if (int.TryParse(config["ReauthMinutes"], out int minutes) && minutes > 0)
                _reauthThresholdMinutes = minutes;

            if (bool.TryParse(config["EnableDevTokenReuse"], out bool devReuse))
                _enableDevTokenReuse = devReuse;


            _consumerName = consumerName ?? throw new ArgumentNullException(nameof(consumerName));
            _logTextBox = logTextBox;
            _experienceService = experienceService;
            _tokenStoreService = tokenStoreService;
            _mainWindow = mainWindow;
            Console.WriteLine($"🔁 Reauth threshold set to {_reauthThresholdMinutes} minutes.");
        }

        // Centralized UI logging shim used throughout this service.
        // MainWindow.LogToUI performs redaction and marshals to UI thread.
        private void LogToUi(string message)
        {
            try
            {
                if (_mainWindow != null)
                {
                    _mainWindow.LogToUI(message);
                }
            }
            catch
            {
                // never throw from logging
            }
        }






        // Token property: Sets and gets the token
        public string Token
        {
            get => _token;
            set
            {
                _token = value;

                var parts = _token.Split('.');

                if (_logTextBox != null)
                {
                    LogToUi($"🧪 Token Part Count: {parts.Length}\n");
                }

                // Debugging: Show the token parts
                if (parts.Length < 2)
                {
                    LogToUi("⚠️ Invalid token format.\n");
                    return;
                }

                // Try to decode payload expiration
                DumpJwtPayload(_token);

                // Fallback to header-based expiration or default
                if (_expirationTime == null)
                {
                    var headerExpiration = GetTokenExpirationTime(); // assumes you have a header decoder method
                    if (headerExpiration != null)
                    {
                        _expirationTime = headerExpiration;
                        LogToUi($"🕓 Token Expiration (from header): {_expirationTime}\n");
                    }
                    else
                    {
                        LogToUi("⚠️ No expiration found in token. Using fallback.\n");
                        _expirationTime = DateTime.UtcNow.AddMinutes(10); // fallback if no exp at all
                    }
                }

                // ✅ Now start monitoring
                StartAuthCheckTimer();
                _ = CheckTokenExpirationAsync();
                AuthStatusChanged?.Invoke();
            }
        }




        private void DumpJwtPayload(string? token)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    Console.WriteLine("⚠️ Token is null or empty.");
                    return;
                }

                var parts = token.Split('.');
                if (parts.Length < 2)
                {
                    Console.WriteLine("❌ Invalid JWT format. Expected at least 2 parts.");
                    return;
                }

                // JWT is typically header.payload.signature; payload is part[1]
                string payload = parts[1];
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

                byte[] jsonBytes = Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/'));
                string json = Encoding.UTF8.GetString(jsonBytes);

                Console.WriteLine("🔎 JWT Payload:");
                Console.WriteLine(json);

                dynamic decoded = JsonConvert.DeserializeObject(json);
                var expUnix = decoded?.exp;
                if (expUnix != null)
                {
                    long expSeconds = (long)expUnix;
                    var expDate = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
                    Console.WriteLine($"🕓 Token Expiration: {expDate:u} ({(expDate - DateTimeOffset.UtcNow).TotalMinutes:F1} mins from now)");

                    // Store expiration time globally
                    _expirationTime = expDate.UtcDateTime;
                }
                else
                {
                    Console.WriteLine("⚠️ No 'exp' claim found in JWT.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error decoding JWT: {ex.Message}");
            }
        }


        private void StartAuthCheckTimer()
        {
            _authCheckTimer?.Dispose();
            if (_expirationTime != null)
            {
                _authCheckTimer = new Timer(async _ => await CheckTokenExpirationAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
                LogToUi($"⏳ Token expiration monitoring started (expires at {_expirationTime})\n");
            }
            else
            {
                LogToUi("⚠️ Skipping token expiration monitoring — no expiration time set.\n");
            }
        }

        public void NotifyTokenTimeRemaining(TimeSpan remaining)
        {
            string formatted = $"{(int)remaining.TotalMinutes}m {remaining.Seconds:D2}s";
            TokenTimeRemainingUpdated?.Invoke(formatted);
        }

        // Function to check token expiration and refresh if necessary
        // Function to check token expiration and refresh if necessary
        public async Task CheckTokenExpirationAsync()
        {
            // No async work currently; keep signature for existing call sites.
            await Task.Yield();

            if (_expirationTime == null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    LogToUi("⚠️ No expiration time available.");
                });
                return;
            }

            var expirationUtc = DateTime.SpecifyKind(_expirationTime.Value, DateTimeKind.Utc);
            var nowUtc = DateTime.UtcNow;

            var timeRemaining = expirationUtc - nowUtc;
            if (timeRemaining < TimeSpan.Zero)
                timeRemaining = TimeSpan.Zero;

            NotifyTokenTimeRemaining(timeRemaining);

            if (timeRemaining <= TimeSpan.Zero)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    LogToUi("❌ Token expired. Logging out.\n");
                });

                TokenTimeRemainingUpdated?.Invoke("Expired");

                // Best practice: do not attempt silent refresh; require explicit login.
                // Also: do not touch UI here (AuthStatusChanged is handled by MainWindow on UI thread).
                Logout();
            }
        }


        // Get the token expiration time from the JWT token
        public DateTime? GetTokenExpirationTime()
        {
            try
            {
                var token = _token;
                if (string.IsNullOrWhiteSpace(token))
                    return null;

                var parts = token.Split('.');
                if (parts.Length < 2)
                {
                    Console.WriteLine("⚠️ Invalid token format (expected at least 2 parts). ");
                    return null;
                }

                string? decodedHeader = null;
                string? decodedPayload = null;

                try { decodedHeader = DecodeBase64Url(parts[0]); } catch { }
                try { decodedPayload = DecodeBase64Url(parts[1]); } catch { }

                // 1E-style: Expiration in header (often ISO timestamp)
                if (!string.IsNullOrWhiteSpace(decodedHeader))
                {
                    try
                    {
                        var headerJson = JObject.Parse(decodedHeader);
                        if (headerJson.TryGetValue("Expiration", StringComparison.OrdinalIgnoreCase, out var expTok) &&
                            expTok != null &&
                            !string.IsNullOrWhiteSpace(expTok.ToString()))
                        {
                            var expStr = expTok.ToString();

                            if (DateTimeOffset.TryParse(expStr, out var dto))
                                return dto.UtcDateTime;

                            if (DateTime.TryParse(expStr, out var dt))
                            {
                                if (dt.Kind == DateTimeKind.Unspecified)
                                    dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

                                return dt.ToUniversalTime();
                            }
                        }
                    }
                    catch
                    {
                        // ignore header parse issues
                    }
                }

                // JWT-style: exp in payload (epoch seconds) -- optional
                if (!string.IsNullOrWhiteSpace(decodedPayload))
                {
                    try
                    {
                        var payloadObj = JObject.Parse(decodedPayload);
                        if (payloadObj.TryGetValue("exp", out var exp) && exp != null)
                        {
                            if (exp.Type == JTokenType.Integer || exp.Type == JTokenType.Float)
                                return DateTimeOffset.FromUnixTimeSeconds(exp.Value<long>()).UtcDateTime;

                            if (long.TryParse(exp.ToString(), out var secondsStr))
                                return DateTimeOffset.FromUnixTimeSeconds(secondsStr).UtcDateTime;
                        }
                    }
                    catch
                    {
                        // ignore payload parse issues
                    }
                }

                Console.WriteLine("⚠️ Expiration field not found or could not be parsed.");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to decode token expiration: {ex.Message}");
                return null;
            }
        }

        public void SetExpirationTime(DateTime expiration)
        {
            if (expiration.Kind == DateTimeKind.Unspecified)
                expiration = DateTime.SpecifyKind(expiration, DateTimeKind.Utc);

            _expirationTime = expiration.ToUniversalTime();
        }



        private string DecodeBase64Url(string input)
        {
            var output = input.Replace('-', '+').Replace('_', '/');
            switch (output.Length % 4)
            {
                case 2: output += "=="; break;
                case 3: output += "="; break;
            }

            var base64Bytes = Convert.FromBase64String(output);
            return Encoding.UTF8.GetString(base64Bytes);
        }

        // Logout function
        public async Task LogoutAsync()
        {
            if (string.IsNullOrWhiteSpace(_token)) return;

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("X-Tachyon-Authenticate", _token);
                client.DefaultRequestHeaders.Add("X-Tachyon-Consumer", _consumerName);

                var url = $"https://{_platformUrl}/tachyon/api/Authentication/Logout";
                await client.GetAsync(url);
                Console.WriteLine("👋 Logged out successfully.");
                DeleteSavedToken(_platformUrl);
                _token = null;
                _expirationTime = null;

                _authCheckTimer?.Dispose();
                _authCheckTimer = null;

                // No _tokenTimer or _cts in your version, so skip them

                AuthStatusChanged?.Invoke();  // ✅ No parameters now
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Logout error: {ex.Message}");
            }
        }

        public event Action? AuthStatusChanged;
        public DateTime? ExpirationTime => _expirationTime;

        public void ApplySettings(bool enableDevTokenReuse, int reauthThresholdMinutes)
        {
            _enableDevTokenReuse = enableDevTokenReuse;
            if (reauthThresholdMinutes > 0)
                _reauthThresholdMinutes = reauthThresholdMinutes;
        }


        public void Logout()
        {
            DeleteSavedToken(_platformUrl);
            _token = null;
            _expirationTime = null;

            _authCheckTimer?.Dispose();
            _authCheckTimer = null;

            // No _tokenTimer or _cts in your version, so skip them

            AuthStatusChanged?.Invoke();  // ✅ No parameters now

            Console.WriteLine("❌ Logged out. Deleting saved token for this platform.");

            if (!string.IsNullOrEmpty(_platformUrl))
            {
                try
                {
                    string tokenFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DexConsole");
                    string tokenFile = Path.Combine(tokenFolder, $"{GetPlatformHash(_platformUrl)}.bin");

                    if (File.Exists(tokenFile))
                        DeleteSavedToken(_platformUrl);
                    //File.Delete(tokenFile);
                }
                catch
                {
                    // Ignore delete errors
                }
            }
        }



        // Function to dump JWT payload and log
        private void DumpCustomTokenPayload(string token)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3)
                {
                    Console.WriteLine("⚠️ Invalid token format.");
                    return;
                }

                var payload = parts[1];
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));

                dynamic obj = JsonConvert.DeserializeObject(json);

                Console.WriteLine("🔍 Token Payload: ");
                Console.WriteLine(JsonConvert.SerializeObject(obj, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error while dumping token payload: {ex.Message}");
            }
        }
        private string GetSavedToken(string platformUrl)
        {
            try
            {
                string machineId = GetMachineId();
                string compositeKey = $"{platformUrl}_{machineId}";
                string hashedKey = ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(compositeKey)));

                LogToUi($"🔍 Generating hashedKey: {hashedKey}\n");  // Debugging line

                var tokenFile = TokenFilePath;

                // If the token file exists, read the lines
                if (File.Exists(tokenFile))
                {
                    var lines = File.ReadAllLines(tokenFile).ToList();

                    // Look for the entry for the specific platform
                    foreach (var line in lines)
                    {
                        //_logTextBox.Text += $"🔍 Checking line: {line}\n";  // Debugging line

                        if (line.StartsWith(hashedKey))
                        {
                            // Extract the encrypted token (after the ":")
                            string encryptedToken = line.Substring(hashedKey.Length + 1);

                            LogToUi($"🔍 Found token for platform: {platformUrl}\n");  // Debugging line

                            // Log the raw encrypted token
                            // _logTextBox.Text += $"🔒 Encrypted token: {encryptedToken}\n";  // Debugging line

                            // Decrypt the token using the same key
                            string decryptedToken = DecryptToken(encryptedToken, hashedKey);

                            // _logTextBox.Text += $"🔓 Decrypted token: {decryptedToken}\n";  // Debugging line

                            return decryptedToken;
                        }
                    }
                }

                // If the token is not found, return null
                return null;
            }
            catch (Exception ex)
            {
                LogToUi($"❌ Error reading token: {ex.Message}\n");
                return null;
            }
        }

        public bool IsMultiTenant { get; private set; }

        private string GetAuthModePath(string platformUrl)
        {
            string key = $"{platformUrl}_{GetMachineId()}";
            string tokenPath = GetTokenPath(key);
            return tokenPath + ".mode";
        }

        private void SaveAuthMode(string platformUrl, bool isMultiTenant)
        {
            if (!_enableDevTokenReuse)
                return;

            try
            {
                var path = GetAuthModePath(platformUrl);
                File.WriteAllText(path, isMultiTenant ? "MT" : "ST");
            }
            catch (Exception ex)
            {
                LogToUi($"❌ Error saving auth mode: {ex.Message}\n");
            }
        }

        private bool TryGetSavedAuthMode(string platformUrl, out bool isMultiTenant)
        {
            isMultiTenant = false;
            if (!_enableDevTokenReuse)
                return false;

            try
            {
                var path = GetAuthModePath(platformUrl);
                if (!File.Exists(path))
                    return false;

                var txt = (File.ReadAllText(path) ?? string.Empty).Trim();
                if (txt.Equals("MT", StringComparison.OrdinalIgnoreCase))
                {
                    isMultiTenant = true;
                    return true;
                }
                if (txt.Equals("ST", StringComparison.OrdinalIgnoreCase))
                {
                    isMultiTenant = false;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogToUi($"❌ Error reading auth mode: {ex.Message}\n");
                return false;
            }
        }

        private void DeleteSavedAuthMode(string platformUrl)
        {
            if (!_enableDevTokenReuse)
                return;

            try
            {
                var path = GetAuthModePath(platformUrl);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }

        private static bool LooksLikeJwt(string token)
        {
            // Multi-tenant auth returns a JWT (three base64url segments separated by dots).
            // Single-tenant auth returns an opaque token string.
            if (string.IsNullOrWhiteSpace(token))
                return false;

            var parts = token.Split('.');
            if (parts.Length != 3)
                return false;

            // Quick sanity: header & payload should be decodable base64url.
            try
            {
                _ = Base64UrlDecodeToBytes(parts[0]);
                _ = Base64UrlDecodeToBytes(parts[1]);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] Base64UrlDecodeToBytes(string base64Url)
        {
            var s = base64Url.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        private enum TenantProbeResult
        {
            Unknown = 0,
            MultiTenant = 1,
            SingleTenant = 2
        }

        private async Task<TenantProbeResult> ProbeTenantModeAsync(string platformUrl)
        {
            if (string.IsNullOrWhiteSpace(platformUrl))
                return TenantProbeResult.Unknown;

            // The user-requested simple heuristic:
            // 1) Try multi-tenant info endpoint first: /api/information
            // 2) If that fails, try single-tenant info endpoint: /tachyon/api/information
            // If both fail, return Unknown and we fall back to original auth attempt order.

            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            try
            {
                var mtUrl = $"https://{platformUrl}/api/information";
                using var mtResp = await http.GetAsync(mtUrl);
                if (mtResp.IsSuccessStatusCode)
                    return TenantProbeResult.MultiTenant;
            }
            catch
            {
                // ignore
            }

            try
            {
                var stUrl = $"https://{platformUrl}/tachyon/api/information";
                using var stResp = await http.GetAsync(stUrl);
                if (stResp.IsSuccessStatusCode)
                    return TenantProbeResult.SingleTenant;
            }
            catch
            {
                // ignore
            }

            return TenantProbeResult.Unknown;
        }

        // Authentication function
        public async Task<AuthenticationResult> AuthenticateAsync(PlatformConfig platform)
        {
            _platformUrl = platform.Url;

            // Try to reuse a previously saved token only when EnableDevTokenReuse is enabled
            if (_enableDevTokenReuse && TryGetSavedToken(_platformUrl, out var savedToken) && !string.IsNullOrWhiteSpace(savedToken))
            {
                Console.WriteLine("🔄 Reusing saved token...");

                // Prefer the persisted auth mode (single vs multi) for this platform.
                // Fall back to token-shape inference only if we have no persisted mode.
                if (!TryGetSavedAuthMode(_platformUrl, out var persistedIsMulti))
                    IsMultiTenant = LooksLikeJwt(savedToken);
                else
                    IsMultiTenant = persistedIsMulti;

                // Setting Token will decode exp + start timer
                Token = savedToken;

                // Ensure expiration is set (fallback if token has no exp)
                SetExpirationTime(_expirationTime ?? DateTime.UtcNow.AddMinutes(10));

                if (_expirationTime <= DateTime.UtcNow)
                {
                    Console.WriteLine("⚠️ Saved token is expired. Skipping reuse.");
                    DeleteSavedToken(_platformUrl);
                    DeleteSavedAuthMode(_platformUrl);
                }
                else
                {
                    return new AuthenticationResult
                    {
                        Token = savedToken,
                        PrincipalName = ExtractPrincipalFromToken(savedToken) ?? Environment.UserName
                    };
                }
            }

            // If the saved token is expired or not found, perform full authentication process.
            // User-requested simple rule:
            //   1) Probe MT first via /api/information. If it responds, authenticate MT.
            //   2) Otherwise authenticate ST via the WebSocket tachyon/api endpoint.
            //   3) If probing is inconclusive, fall back to the original order (ST then MT).

            string token;
            var probe = await ProbeTenantModeAsync(platform.Url);
            if (probe == TenantProbeResult.MultiTenant)
            {
                Console.WriteLine("🔎 /api/information indicates Multi-Tenant. Authenticating MT...");
                token = await AuthenticateMultiTenant(platform.Url);
                IsMultiTenant = !string.IsNullOrWhiteSpace(token);
            }
            else if (probe == TenantProbeResult.SingleTenant)
            {
                string socketUrl = $"wss://{platform.Url}/tachyon/api/Authentication/RequestAuthentication";
                Console.WriteLine($"🔎 /tachyon/api/information indicates Single-Tenant. Authenticating ST via WebSocket: {socketUrl}");
                token = await AuthenticateSingleTenant(socketUrl);
                IsMultiTenant = false;
            }
            else
            {
                // Fallback: original behavior
                string socketUrl = $"wss://{platform.Url}/tachyon/api/Authentication/RequestAuthentication";
                Console.WriteLine($"🔐 Trying single-tenant auth via WebSocket: {socketUrl}");

                token = await AuthenticateSingleTenant(socketUrl);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    IsMultiTenant = false;
                }
                else
                {
                    Console.WriteLine("❌ Single-Tenant failed. Trying Multi-Tenant...");
                    token = await AuthenticateMultiTenant(platform.Url);
                    if (!string.IsNullOrWhiteSpace(token))
                        IsMultiTenant = true;
                }
            }


            if (!string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("✅ Authentication succeeded.");
                _platformUrl = platform.Url;
                this.Token = token;
                SaveToken(platform.Url, token); // ✅ Correct
                SaveAuthMode(platform.Url, IsMultiTenant);
                ; // Save fresh token (encrypted)
                return new AuthenticationResult
                {
                    Token = token,
                    PrincipalName = ExtractPrincipalFromToken(token) ?? Environment.UserName
                };
            }

            return new AuthenticationResult { Token = string.Empty, PrincipalName = string.Empty };
        }



        // Multi-Tenant Authentication
        private async Task<string> AuthenticateSingleTenant(string socketUrl)
        {
            using var ws = new ClientWebSocket();
            ws.Options.UseDefaultCredentials = true;
            CancellationToken ct = CancellationToken.None;

            try
            {
                await ws.ConnectAsync(new Uri(socketUrl), ct);
                byte[] command = Encoding.UTF8.GetBytes("ACTION=Command");
                await ws.SendAsync(new ArraySegment<byte>(command), WebSocketMessageType.Text, true, ct);

                var buffer = new byte[1024];
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                string response = Encoding.UTF8.GetString(buffer, 0, result.Count);

                var status = TryGetStatusString(response);
                if (string.Equals(status, "AuthenticationRequested", StringComparison.OrdinalIgnoreCase))
                {
                    var authUrl = TryGetDataString(response);
                    if (!string.IsNullOrWhiteSpace(authUrl))
                    {
                        OpenBrowser(authUrl);
                        return await PollForToken(ws, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ WebSocket auth failed: {ex.Message}");
            }

            return string.Empty;
        }

        private async Task<string> PollForToken(ClientWebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[1024];
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                string response = Encoding.UTF8.GetString(buffer, 0, result.Count);

                if (response.Contains("AuthenticationSuccessful"))
                {
                    var token = TryGetDataString(response);
                    return token ?? string.Empty;
                }

                await Task.Delay(1000);
            }

            return string.Empty;
        }

        // Multi-Tenant Authentication
        private async Task<string> AuthenticateMultiTenant(string platformUrl)
        {
            byte[] nonceBytes = new byte[32];
            new Random().NextBytes(nonceBytes);
            string nonceEncoded = Convert.ToBase64String(nonceBytes);
            byte[] nonceHashed = SHA256.HashData(Convert.FromBase64String(nonceEncoded));
            string nonceHashEncoded = ToBase64Url(nonceHashed);

            string url = $"https://{platformUrl}/tachyon/api/Authentication/RequestAuthentication?ecpn={nonceHashEncoded}";

            using var client = new HttpClient();
            var response = await client.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();

            dynamic authResponse = JsonConvert.DeserializeObject(json);
            if (authResponse is Newtonsoft.Json.Linq.JArray arr)
                authResponse = arr[0];

            string authUrl = authResponse?.AuthenticationUrl;
            string state = authResponse?.State;
            if (string.IsNullOrEmpty(authUrl) || string.IsNullOrEmpty(state)) return string.Empty;

            OpenBrowser(authUrl);

            string pollUrl = $"https://{platformUrl}/tachyon/api/Authentication/CheckAuthenticationState";
            return await PollForToken(client, pollUrl, state, nonceEncoded);
        }

        private async Task<string> PollForToken(HttpClient client, string url, string state, string nonceEncoded)
        {
            string payload = $"{{ \"state\" : \"{state}\", \"nonce\" : null }}";
            string responseJson = await PostHttpRequest(client, url, payload);

            while (!responseJson.Contains("AuthenticationSuccessful"))
            {
                await Task.Delay(1000);
                responseJson = await PostHttpRequest(client, url, payload);
            }

            payload = $"{{ \"state\" : \"{state}\", \"nonce\" : \"{nonceEncoded}\" }}";
            responseJson = await PostHttpRequest(client, url, payload);

            var data = TryGetDataString(responseJson);
            return data ?? string.Empty;
        }


        private static JObject? TryParseObjectOrFirstObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var token = JToken.Parse(json);

                if (token is JArray arr)
                {
                    if (arr.Count == 0)
                        return null;

                    token = arr[0];
                }

                return token as JObject;
            }
            catch
            {
                return null;
            }
        }

        private static string? TryGetDataString(string json)
        {
            var obj = TryParseObjectOrFirstObject(json);
            if (obj == null)
                return null;

            var data = obj["Data"] ?? obj["data"];
            return data?.ToString();
        }

        private static string? TryGetStatusString(string json)
        {
            var obj = TryParseObjectOrFirstObject(json);
            if (obj == null)
                return null;

            var status = obj["Status"] ?? obj["status"];
            return status?.ToString();
        }


        private static async Task<string> PostHttpRequest(HttpClient client, string url, string payload)
        {
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            return await response.Content.ReadAsStringAsync();
        }

        private static string? ExtractPrincipalFromToken(string token)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3) return null;

                var payload = parts[1];
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                var bytes = Convert.FromBase64String(payload);
                var json = Encoding.UTF8.GetString(bytes);

                dynamic obj = JsonConvert.DeserializeObject(json);
                return obj?.unique_name ?? obj?.upn ?? obj?.name ?? null;
            }
            catch
            {
                return null;
            }
        }
        private static string GetMachineId()
        {
            try
            {
                var networkInterfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                var firstUpInterface = networkInterfaces
                    .FirstOrDefault(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback);

                if (firstUpInterface != null)
                {
                    return BitConverter.ToString(firstUpInterface.GetPhysicalAddress().GetAddressBytes()).Replace("-", "");
                }
            }
            catch
            {
                // fallback if needed
            }

            // fallback: random per-install GUID if MAC not available (rare edge case)
            var fallbackId = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DexConsole", "machineid.txt");
            if (File.Exists(fallbackId))
                return File.ReadAllText(fallbackId);

            var newId = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fallbackId));
            File.WriteAllText(fallbackId, newId);
            return newId;
        }

        private static string ToBase64Url(byte[] input)
        {
            return Convert.ToBase64String(input)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }

        private static string GetSafeHash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha256.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant().Substring(0, 16); // short 16 characters
        }

        private readonly string _tokenFilePath = Path.Combine(AppContext.BaseDirectory, "tvappdata.bin");




        public void SaveToken(string platformUrl, string token)
        {
            if (!_enableDevTokenReuse)
            {
                LogToUi("ℹ️ Dev token reuse disabled; not saving token.\n");
                return;
            }

            // Token caching is optional and Windows-only (DPAPI). On macOS/Linux we simply skip caching
            // to avoid any platform-specific crypto/runtime issues.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                LogToUi("ℹ️ Token caching is disabled on non-Windows platforms.\n");
                return;
            }

            try
            {
                string key = $"{platformUrl}_{GetMachineId()}";
                string path = GetTokenPath(key);

                byte[] encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(token),
                    null,
                    DataProtectionScope.CurrentUser);

                File.WriteAllBytes(path, encrypted);
                LogToUi($"✅ Token saved securely for {platformUrl}\n");
            }
            catch (Exception ex)
            {
                LogToUi($"❌ Error saving token: {ex.Message}\n");
            }
        }


        public bool TryGetSavedToken(string platformUrl, out string token)
        {
            if (!_enableDevTokenReuse)
            {
                token = null;
                return false;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                token = null;
                return false;
            }

            token = null;
            try
            {
                string key = $"{platformUrl}_{GetMachineId()}";
                string path = GetTokenPath(key);

                if (File.Exists(path))
                {
                    byte[] encrypted = File.ReadAllBytes(path);
                    byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    token = Encoding.UTF8.GetString(decrypted);
                    LogToUi($"✅ Token loaded for {platformUrl}\n");
                    return true;
                }

                LogToUi($"ℹ️ No token file found for {platformUrl}\n");
                return false;
            }
            catch (Exception ex)
            {
                LogToUi($"❌ Error retrieving token: {ex.Message}\n");
                return false;
            }
        }

        public void DeleteSavedToken(string platformUrl)
        {
            if (!_enableDevTokenReuse)
            {
                return;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            try
            {
                string key = $"{platformUrl}_{GetMachineId()}";
                string path = GetTokenPath(key);

                if (File.Exists(path))
                {
                    File.Delete(path);
                    LogToUi($"✅ Token deleted for {platformUrl}\n");
                }
                else
                {
                    LogToUi($"ℹ️ No token file to delete for {platformUrl}\n");
                }

                // Keep mode file in sync with token lifetime.
                DeleteSavedAuthMode(platformUrl);
            }
            catch (Exception ex)
            {
                LogToUi($"❌ Error deleting token: {ex.Message}\n");
            }
        }

        private string GetTokenPath(string key)
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DexConsole",
                "Tokens");

            Directory.CreateDirectory(folder); // Ensure the folder exists
            return Path.Combine(folder, $"{key}.token");
        }



        /*
        private void SaveToken(string token)
        {
            try
            {
                if (!Directory.Exists(_tokenFolderPath))
                    Directory.CreateDirectory(_tokenFolderPath);

                string machineId = GetMachineId();
                string compositeKey = $"{_platformUrl}_{machineId}";
                string hashedKey = ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(compositeKey)));

                // Encrypt the token before saving it
                string encryptedToken = EncryptToken(token, hashedKey);

                var tokenFile = TokenFilePath;
                var lines = new List<string>();

                if (File.Exists(tokenFile))
                    lines = File.ReadAllLines(tokenFile).ToList();

                // Remove old entry for this platform
                lines.RemoveAll(l => l.StartsWith(hashedKey));

                // Add the encrypted token
                lines.Add($"{hashedKey}:{encryptedToken}");

                // Write the encrypted token to the file
                File.WriteAllLines(tokenFile, lines);
            }
            catch (Exception ex)
            {
                LogToUi($"❌ Error saving token: {ex.Message}\n");
            }
        }


        public bool TryGetSavedToken(string platformUrl, out string token)
        {
            token = null;
            try
            {
                string machineId = GetMachineId();
                string compositeKey = $"{platformUrl}_{machineId}";
                string hashedKey = ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(compositeKey)));

                // Debugging output to ensure correct hashedKey generation
                LogToUi($"🔍 Looking for token with hashedKey: {hashedKey}\n");  // Debug line

                var tokenFile = TokenFilePath;

                // If the token file exists, read the lines
                if (File.Exists(tokenFile))
                {
                    var lines = File.ReadAllLines(tokenFile).ToList();

                    // Look for the entry for the specific platform
                    foreach (var line in lines)
                    {
                        //_logTextBox.Text += $"🔍 Checking line: {line}\n";  // Debug line

                        if (line.StartsWith(hashedKey))
                        {
                            string encryptedToken = line.Substring(hashedKey.Length + 1);  // Remove hashedKey and separator

                            // Debugging: Print the found token line
                          //  _logTextBox.Text += $"🔍 Found token line: {line}\n";  // Debug line

                            // Decrypt the token using the same key
                            string decryptedToken = DecryptToken(encryptedToken, hashedKey);

                            token = decryptedToken;
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                LogToUi($"❌ Error reading token: {ex.Message}\n");
                return false;
            }
        }


        public void DeleteSavedToken(string platformUrl)
        {
            try
            {
                // Use TokenStoreService to remove the token
                _tokenStoreService.RemoveToken(platformUrl);

                LogToUi($"✅ Token3 for platform {platformUrl} deleted successfully.\n");
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    LogToUi($"❌ Error deleting token: {ex.Message}\n");
                });

            }
        }
        */

        public void DeleteToken()
        {
            try
            {
                string tokenFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DexConsole", "tvappdata.bin");

                if (File.Exists(tokenFilePath))
                {
                    // Log the start of the operation
                    LogToUi("🔒 Deleting token...\n");
                    // Read the file
                    var json = File.ReadAllText(tokenFilePath);
                    var tokens = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);

                    // If tokens exist, proceed with removal
                    if (tokens != null)
                    {
                        // If you know the key (e.g., platform URL or hash), remove that specific entry
                        string hash = GetSafeHash(_platformUrl);  // Assuming platform URL or a similar key to identify the token
                        tokens.Remove(hash);  // Remove the token entry

                        // Save the modified token file back
                        var updatedJson = JsonConvert.SerializeObject(tokens, Formatting.Indented);
                        File.WriteAllText(tokenFilePath, updatedJson);

                        LogToUi("✅ Token2 deleted successfully.\n");
                    }
                    else
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            LogToUi("⚠️ No tokens found to delete.\n");
                        });
                    }
                }
                else
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        LogToUi("⚠️ Token file not found.\n");
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    LogToUi($"❌ Error deleting token: {ex.Message}\n");
                });
            }
        }




        private string DecodeBase64(string input)
        {
            var output = input.Replace('-', '+').Replace('_', '/'); // Standardize the URL base64 format
            while (output.Length % 4 != 0)
            {
                output += "="; // Padding if necessary
            }
            byte[] decodedBytes = Convert.FromBase64String(output);
            return Encoding.UTF8.GetString(decodedBytes);
        }


        private static string GetPlatformHash(string platformUrl)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(platformUrl);
                byte[] hash = sha.ComputeHash(bytes);
                return Convert.ToBase64String(hash)
                    .Replace("/", "_")
                    .Replace("+", "-")
                    .TrimEnd('=');
            }
        }



        private static void OpenBrowser(string url)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    Process.Start("open", url);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    Process.Start("xdg-open", url);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening browser: {ex.Message}");
            }
        }
        private static string EncryptToken(string token, string key)
        {
            using var aes = Aes.Create();
            var keyBytes = Encoding.UTF8.GetBytes(key.PadRight(32).Substring(0, 32));  // Ensure the key is 32 bytes long
            aes.Key = keyBytes;
            aes.GenerateIV();  // Generate a random IV (Initialization Vector)

            using var encryptor = aes.CreateEncryptor();
            var tokenBytes = Encoding.UTF8.GetBytes(token);
            var encrypted = encryptor.TransformFinalBlock(tokenBytes, 0, tokenBytes.Length);

            // Combine the IV and the encrypted token, and return the result as a Base64 string
            return Convert.ToBase64String(aes.IV.Concat(encrypted).ToArray());
        }

        private static string DecryptToken(string encryptedToken, string key)
        {
            var fullCipher = Convert.FromBase64String(encryptedToken);

            using var aes = Aes.Create();
            var keyBytes = Encoding.UTF8.GetBytes(key.PadRight(32).Substring(0, 32));  // Ensure the key is 32 bytes long
            aes.Key = keyBytes;
            aes.IV = fullCipher.Take(16).ToArray();  // The first 16 bytes are the IV

            using var decryptor = aes.CreateDecryptor();
            var cipherText = fullCipher.Skip(16).ToArray();  // The remaining bytes are the encrypted token

            var decryptedBytes = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
            return Encoding.UTF8.GetString(decryptedBytes);  // Return the decrypted token
        }





        public Task<bool> RefreshTokenAsync()
        {
            // Best practice: do not silently refresh bearer tokens.
            // Force a re-login instead.
            Logout();
            return Task.FromResult(false);
        }




    }
}