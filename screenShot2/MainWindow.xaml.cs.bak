using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Media;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;

namespace screenShot2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _isCapturing = false;
        private string _saveFolderPath = string.Empty;
        private int _screenshotCount = 0;
        private static readonly HttpClient _httpClient = new HttpClient();
        private List<string> _currentScreenshotPaths = new List<string>();
        private Random _random = new Random();

        // Gemini CLI (APIレート回避のため優先利用)
        private bool _useGeminiCli = true; // true ならCLIを優先し、失敗時のみHTTP APIへフォールバック
        private string _geminiCliCommand = @"C:\\nvm4w\\nodejs\\gemini.cmd"; // 環境に応じて変更。PATHが通っていれば "gemini" でも可
        private bool _geminiCliAvailable = false;

        // ポイント制介入システム
        private int _violationPoints = 0;
        private bool _isDelayEnabled = false;
        private bool _isGrayscaleEnabled = false;
        private bool _isMouseInverted = false;
        private System.Windows.Forms.Timer? _moveTimer;
        private System.Windows.Forms.Timer? _mouseInversionTimer;
        private System.Drawing.Point _lastMousePos;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;

        // キーボードフック関連
        private static LowLevelKeyboardProc? _keyboardProc;
        private static IntPtr _keyboardHookID = IntPtr.Zero;
        private bool _isSending = false;

        // 画面ロック用のWin32 API
        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();

        // マウス操作用のWin32 API
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        // グレースケール用のWin32 API
        [DllImport("Magnification.dll", SetLastError = true)]
        private static extern bool MagInitialize();

        [DllImport("Magnification.dll", SetLastError = true)]
        private static extern bool MagUninitialize();

        [DllImport("Magnification.dll", SetLastError = true)]
        private static extern bool MagSetFullscreenColorEffect(ref MAGCOLOREFFECT pEffect);

        private struct MAGCOLOREFFECT
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            public float[] transform;
        }

        // キーボードフック用のWin32 API
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public MainWindow()
        {
            InitializeComponent();
            InitializeApp();
            InitializeInterventionSystem();
        }

        private void InitializeInterventionSystem()
        {
            // マウス反転用タイマー
            _moveTimer = new System.Windows.Forms.Timer();
            _moveTimer.Interval = 2;
            _moveTimer.Tick += MoveTimer_Tick;

            // マウス反転30秒自動解除タイマー
            _mouseInversionTimer = new System.Windows.Forms.Timer();
            _mouseInversionTimer.Interval = 30000; // 30秒
            _mouseInversionTimer.Tick += MouseInversionTimer_Tick;

            // 通知アイコンの初期化
            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Warning,
                Visible = true,
                Text = "ScreenShot2 Monitor"
            };

            // キーボードフックのデリゲート設定
            _keyboardProc = KeyboardHookCallback;
        }

        private void InitializeApp()
        {
            // デフォルトの保存先を Pictures/capture に設定
            _saveFolderPath = @"C:\Users\it222104\Pictures\capture";
            FolderPathTextBox.Text = _saveFolderPath;

            // モニター情報を更新
            UpdateMonitorInfo();

            // 設定ファイルからAPIキーを読み込む
            LoadSettings();

            // Gemini CLI 接続チェック（非同期で実施）
            _ = CheckGeminiCliConnectionAsync();
        }

        private void UpdateMonitorInfo()
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            MonitorInfoTextBlock.Text = $"検出されたモニター: {screens.Length}";
            AddLog($"モニター数: {screens.Length}");

            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                AddLog($"  モニター {i + 1}: {screen.Bounds.Width}x{screen.Bounds.Height} " +
                       $"{(screen.Primary ? "(メイン)" : "")}");
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "スクリーンショットの保存先フォルダを選択してください",
                SelectedPath = _saveFolderPath
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _saveFolderPath = dialog.SelectedPath;
                FolderPathTextBox.Text = _saveFolderPath;
                AddLog($"保存先を変更: {_saveFolderPath}");
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_saveFolderPath))
            {
                System.Windows.MessageBox.Show("保存先フォルダを選択してください", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 保存先フォルダを作成
            try
            {
                Directory.CreateDirectory(_saveFolderPath);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"フォルダの作成に失敗しました: {ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _screenshotCount = 0;
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            BrowseButton.IsEnabled = false;

            StatusTextBlock.Text = "撮影中...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            AddLog($"撮影開始 - 45秒サイクル（ランダム撮影）");

            _isCapturing = true;

            // 撮影ループを開始
            _ = StartCaptureLoop();
        }

        private async Task StartCaptureLoop()
        {
            const int cycleMs = 20000; // 45秒サイクル

            while (_isCapturing)
            {
                try
                {
                    // サイクル内のランダムなタイミングで撮影
                    int delay = _random.Next(1000, cycleMs - 1000); // 最初と最後の1秒は避ける余裕を持たせる
                    AddLog($"次の撮影まで待機: {delay / 1000.0:F1}秒");

                    await Task.Delay(delay);

                    if (!_isCapturing) break;

                    // 撮影実行
                    CaptureAllScreens();

                    // サイクルの残り時間を待機
                    int remaining = cycleMs - delay;
                    if (remaining > 0)
                    {
                        await Task.Delay(remaining);
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"ループエラー: {ex.Message}");
                    await Task.Delay(5000); // エラー時は少し待機
                }
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
        }

        private void StopCapture()
        {
            _isCapturing = false;

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            BrowseButton.IsEnabled = true;

            StatusTextBlock.Text = "停止";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Gray);
            AddLog($"撮影停止 - 合計 {_screenshotCount} 枚撮影");
        }

        private async void CaptureAllScreens()
        {
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // 撮影したスクリーンショットのパスを保存
                _currentScreenshotPaths.Clear();

                for (int i = 0; i < screens.Length; i++)
                {
                    var screen = screens[i];
                    string filePath = CaptureScreen(screen, i, timestamp);
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        _currentScreenshotPaths.Add(filePath);
                    }
                }

                _screenshotCount += screens.Length;
                StatusTextBlock.Text = $"撮影中... (合計: {_screenshotCount}枚)";
                AddLog($"スクリーンショット撮影完了: {screens.Length}枚 ({timestamp})");

                // 送信対象の画像パスをログに表示して確認できるようにする
                if (_currentScreenshotPaths.Count > 0)
                {
                    AddLog("送信予定の画像パス一覧:");
                    foreach (var path in _currentScreenshotPaths)
                    {
                        AddLog($"  {path}");
                    }
                }

                // Gemini分析へ送信（常に有効）
                if (_currentScreenshotPaths.Count > 0)
                {
                    await AnalyzeScreenshotsWithGemini(_currentScreenshotPaths);
                }
                // 分析が終わった後に画像を完全削除する処理
                foreach (var path in _currentScreenshotPaths)
                {
                    if (System.IO.File.Exists(path))
                    {
                        try
                        {
                            System.IO.File.Delete(path); // これで完全削除されます（ゴミ箱には行きません）
                            AddLog($"画像を削除しました: {System.IO.Path.GetFileName(path)}");
                        }
                        catch (Exception ex)
                        {
                            AddLog($"削除エラー: {ex.Message}");
                        }
                    }
                }
            }


            catch (Exception ex)
            {
                AddLog($"エラー: {ex.Message}");
            }
        }

        private string CaptureScreen(System.Windows.Forms.Screen screen, int screenIndex, string timestamp)
        {
            var bounds = screen.Bounds;

            // ビットマップを作成（画面の実際の解像度に合わせる）
            using (var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    // DPIを考慮した設定
                    graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                    // 画面をキャプチャ（座標を正確に指定）
                    graphics.CopyFromScreen(
                        bounds.Left,
                        bounds.Top,
                        0,
                        0,
                        new System.Drawing.Size(bounds.Width, bounds.Height),
                        CopyPixelOperation.SourceCopy);
                }

                // ファイル名を生成
                string monitorName = screen.Primary ? "Main" : $"Monitor{screenIndex + 1}";
                string fileName = $"Screenshot_{timestamp}_{monitorName}.png";
                string filePath = System.IO.Path.Combine(_saveFolderPath, fileName);

                // PNG形式で保存
                bitmap.Save(filePath, ImageFormat.Png);

                AddLog($"  {monitorName}: {bounds.Width}x{bounds.Height} at ({bounds.Left}, {bounds.Top})");

                return filePath;
            }
        }

        private void AddLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logMessage = $"[{timestamp}] {message}\n";

            Dispatcher.Invoke(() =>
            {
                LogTextBlock.AppendText(logMessage);
                LogTextBlock.ScrollToEnd();

                // ログが長くなりすぎないように制限
                if (LogTextBlock.Text.Length > 5000)
                {
                    var lines = LogTextBlock.Text.Split('\n');
                    LogTextBlock.Text = string.Join('\n', lines.Skip(lines.Length / 2));
                    LogTextBlock.ScrollToEnd();
                }
            });
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _isCapturing = false;
            _moveTimer?.Stop();
            _mouseInversionTimer?.Stop();
            if (_isGrayscaleEnabled) MagUninitialize();
            if (_keyboardHookID != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHookID);
            _notifyIcon?.Dispose();

            // 設定ファイルにAPIキーを保存する
            SaveSettings();

            base.OnClosing(e);
        }

        // 入力遅延を有効化
        private void EnableInputDelay()
        {
            if (_isDelayEnabled) return;

            try
            {
                using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
                using (var curModule = curProcess.MainModule)
                {
                    if (curModule != null)
                    {
                        _keyboardHookID = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc!,
                            GetModuleHandle(curModule.ModuleName), 0);

                        if (_keyboardHookID != IntPtr.Zero)
                        {
                            _isDelayEnabled = true;
                            AddLog("入力遅延を有効化しました（キーボードフック起動）");
                        }
                        else
                        {
                            AddLog("キーボードフックの設定に失敗しました");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"入力遅延エラー: {ex.Message}");
            }
        }

        // 入力遅延を無効化
        private void DisableInputDelay()
        {
            if (!_isDelayEnabled) return;

            if (_keyboardHookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHookID);
                _keyboardHookID = IntPtr.Zero;
                _isDelayEnabled = false;
                AddLog("入力遅延を解除しました");
            }
        }

        // グレースケールを解除
        private void DisableGrayscale()
        {
            if (!_isGrayscaleEnabled) return;

            try
            {
                MagUninitialize();
                _isGrayscaleEnabled = false;
                AddLog("グレースケールを解除しました");
            }
            catch (Exception ex)
            {
                AddLog($"グレースケール解除エラー: {ex.Message}");
            }
        }

        // マウス反転を強制解除
        private void DisableMouseInversion()
        {
            if (!_isMouseInverted) return;

            _mouseInversionTimer?.Stop();
            _moveTimer?.Stop();
            _isMouseInverted = false;
            AddLog("マウス反転を解除しました");
        }

        // キーボードフックのコールバック
        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN && _isDelayEnabled && !_isSending)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                System.Windows.Forms.Keys key = (System.Windows.Forms.Keys)vkCode;

                bool isAlpha = (vkCode >= (int)System.Windows.Forms.Keys.A && vkCode <= (int)System.Windows.Forms.Keys.Z);
                bool isNumber = (vkCode >= (int)System.Windows.Forms.Keys.D0 && vkCode <= (int)System.Windows.Forms.Keys.D9);

                if (isAlpha || isNumber)
                {
                    // 別スレッドで1秒遅延後に文字を送信
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    {
                        System.Threading.Thread.Sleep(1000);
                        try
                        {
                            _isSending = true;
                            string sendStr = isAlpha ? key.ToString().ToLower() : key.ToString().Replace("D", "");
                            System.Windows.Forms.SendKeys.SendWait(sendStr);
                        }
                        catch (Exception ex)
                        {
                            AddLog($"SendKeys エラー: {ex.Message}");
                        }
                        finally
                        {
                            _isSending = false;
                        }
                    });
                    return (IntPtr)1; // キーを消費
                }
            }
            return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
        }

        // ポイントレベルに応じた介入処理
        private async Task ApplyInterventionLevel()
        {
            string interventionMessage = "";
            string userGoal = string.IsNullOrWhiteSpace(RulesTextBox.Text) ? "設定された目標" : RulesTextBox.Text;
            var goalSummary = userGoal.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "目標";
            if (goalSummary.Length > 50) goalSummary = goalSummary.Substring(0, 50) + "...";

            // レベル1: 通知 (1-30pt)
            if (_violationPoints >= 1 && _violationPoints <= 30)
            {
                interventionMessage = "📢 レベル1: 警告通知";
                ShowNotification($"あなたの目標は「{goalSummary}」です。やるべきことに戻りましょう。");
            }
            else if (_violationPoints <= 0)
            {
                return;
            }
            // レベル2: 入力遅延 (31-60pt)
            else if (_violationPoints <= 60)
            {
                interventionMessage = "⏱️ レベル2: 入力遅延開始";
                if (!_isDelayEnabled) EnableInputDelay();
                ShowNotification($"レベル2: 入力遅延が発生しています。目標: {goalSummary}");
            }
            // レベル3: グレースケール (61-100pt)
            else if (_violationPoints <= 100)
            {
                interventionMessage = "🎨 レベル3: グレースケール適用";
                if (!_isDelayEnabled) EnableInputDelay();
                if (!_isGrayscaleEnabled) ApplyGrayscale();
                ShowNotification($"レベル3: グレースケール適用中。目標: {goalSummary}");
            }
            // レベル4: マウス反転 (101-150pt)
            else if (_violationPoints <= 150)
            {
                interventionMessage = "🖱️ レベル4: マウス反転開始";
                if (!_isDelayEnabled) EnableInputDelay();
                if (!_isGrayscaleEnabled) ApplyGrayscale();
                if (!_isMouseInverted) EnableMouseInversion();
                ShowNotification($"レベル4: マウス反転中。目標: {goalSummary}");
            }
            // レベル5: ビープ音 (151-200pt)
            else if (_violationPoints <= 200)
            {
                interventionMessage = "🔔 レベル5: ビープ音";
                if (!_isDelayEnabled) EnableInputDelay();
                if (!_isGrayscaleEnabled) ApplyGrayscale();
                if (!_isMouseInverted) EnableMouseInversion();
                await PlayForcedAlertAsync();
            }
            // レベル6: 画面ロック (201-250pt)
            else if (_violationPoints <= 250)
            {
                interventionMessage = "🔒 レベル6: 画面ロック実行";
                AddLog("⚠️ ポイント上限！3秒後に画面をロックします...");
                Dispatcher.Invoke(() =>
                {
                    ResultTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 🔒 3秒後に画面ロック実行...");
                    ResultTextBox.AppendText(Environment.NewLine);
                    ResultTextBox.CaretIndex = ResultTextBox.Text.Length;
                    ResultTextBox.ScrollToEnd();
                });
                await Task.Delay(3000);
                LockWorkStation();
                AddLog("画面ロック実行完了");
                return;
            }
            // レベル7: シャットダウン (251pt以上)
            else
            {
                interventionMessage = "💻 レベル7: 強制シャットダウン";
                AddLog("⚠️⚠️⚠️ 最終警告！5秒後にシャットダウンします！");
                Dispatcher.Invoke(() =>
                {
                    ResultTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 💻 5秒後に強制シャットダウン...");
                    ResultTextBox.AppendText(Environment.NewLine);
                    ResultTextBox.CaretIndex = ResultTextBox.Text.Length;
                    ResultTextBox.ScrollToEnd();
                });
                await Task.Delay(5000);
                ForceShutdown();
                return;
            }

            AddLog(interventionMessage);
            Dispatcher.Invoke(() =>
            {
                ResultTextBox.AppendText($"介入レベル: {interventionMessage}");
                ResultTextBox.AppendText(Environment.NewLine);
                ResultTextBox.CaretIndex = ResultTextBox.Text.Length;
                ResultTextBox.ScrollToEnd();
            });
        }

        private void ShowNotification(string message)
        {
            _notifyIcon?.ShowBalloonTip(3000, "警告: ルールを守りましょう", message, System.Windows.Forms.ToolTipIcon.Warning);
        }

        // Gemini CLI 接続チェック（手動ボタン用）
        private async void CheckGeminiCliButton_Click(object sender, RoutedEventArgs e)
        {
            await CheckGeminiCliConnectionAsync();
        }

        private async Task CheckGeminiCliConnectionAsync()
        {
            UpdateGeminiCliStatus("CLI: 確認中...", System.Windows.Media.Colors.DarkGoldenrod);

            string result = await RunCommandAsync(_geminiCliCommand, "--version", Environment.CurrentDirectory);

            if (!string.IsNullOrWhiteSpace(result) && !result.Contains("error", StringComparison.OrdinalIgnoreCase) && !result.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                _geminiCliAvailable = true;
                UpdateGeminiCliStatus($"CLI: 接続OK ({result.Trim()})", System.Windows.Media.Colors.ForestGreen);
            }
            else
            {
                _geminiCliAvailable = false;
                UpdateGeminiCliStatus("CLI: 接続NG (パスを確認してください)", System.Windows.Media.Colors.IndianRed);
            }
        }

        private void UpdateGeminiCliStatus(string text, System.Windows.Media.Color color)
        {
            Dispatcher.Invoke(() =>
            {
                GeminiCliStatusTextBlock.Text = text;
                GeminiCliStatusTextBlock.Foreground = new SolidColorBrush(color);
            });
        }

        // ビープ音を鳴らす
        private async Task PlayBeepAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Console.Beep(1000, 300);
                        System.Threading.Thread.Sleep(200);
                    }
                });
            }
            catch (Exception ex)
            {
                AddLog($"ビープ音エラー: {ex.Message}");
            }
        }

        // 強制的に音量を操作してアラートを鳴らす
        private async Task PlayForcedAlertAsync(float targetVolume = 0.8f)
        {
            MMDevice? device = null;
            float originalVolume = 0;
            bool originalMute = false;
            bool stateSaved = false;

            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                }

                if (device != null)
                {
                    originalVolume = device.AudioEndpointVolume.MasterVolumeLevelScalar;
                    originalMute = device.AudioEndpointVolume.Mute;
                    stateSaved = true;

                    device.AudioEndpointVolume.Mute = false;
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = targetVolume;

                    AddLog($"🔊 アラート再生: 音量を強制的に {targetVolume * 100:F0}% に設定しました");
                }
            }
            catch (Exception ex)
            {
                AddLog($"オーディオデバイス操作エラー: {ex.Message}");
            }

            try
            {
                await Task.Run(() =>
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Console.Beep(2000, 200);
                        Console.Beep(1000, 200);
                    }
                });
            }
            catch (Exception ex)
            {
                AddLog($"ビープ音再生エラー: {ex.Message}");
            }
            finally
            {
                if (device != null && stateSaved)
                {
                    try
                    {
                        device.AudioEndpointVolume.Mute = originalMute;
                        device.AudioEndpointVolume.MasterVolumeLevelScalar = originalVolume;
                    }
                    catch (Exception ex)
                    {
                        AddLog($"音量復元エラー: {ex.Message}");
                    }
                    device = null;
                }
            }
        }

        // グレースケールを適用
        private void ApplyGrayscale()
        {
            if (_isGrayscaleEnabled) return;

            try
            {
                if (!MagInitialize())
                {
                    AddLog("グレースケール初期化失敗（管理者権限が必要）");
                    return;
                }

                var matrix = new MAGCOLOREFFECT
                {
                    transform = new float[25]
                    {
                        0.3f,0.3f,0.3f,0,0,
                        0.6f,0.6f,0.6f,0,0,
                        0.1f,0.1f,0.1f,0,0,
                        0,0,0,1,0,
                        0,0,0,0,1
                    }
                };

                if (!MagSetFullscreenColorEffect(ref matrix))
                {
                    AddLog("グレースケール適用失敗");
                    MagUninitialize();
                    return;
                }

                _isGrayscaleEnabled = true;
                AddLog("グレースケールを適用しました");
            }
            catch (Exception ex)
            {
                AddLog($"グレースケールエラー: {ex.Message}");
            }
        }

        // マウス反転を有効化
        private void EnableMouseInversion()
        {
            if (_isMouseInverted) return;

            _isMouseInverted = true;
            GetCursorPos(out POINT p);
            _lastMousePos = new System.Drawing.Point(p.X, p.Y);
            _moveTimer?.Start();
            _mouseInversionTimer?.Start();
            AddLog("マウス反転を開始しました（30秒後に自動解除）");
        }

        // マウス反転処理
        private void MoveTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isMouseInverted) return;

            GetCursorPos(out POINT current);
            var cur = new System.Drawing.Point(current.X, current.Y);

            int dx = cur.X - _lastMousePos.X;
            int dy = cur.Y - _lastMousePos.Y;

            int newX = cur.X - (int)(dx * 1.5);
            int newY = cur.Y - (int)(dy * 1.5);

            SetCursorPos(newX, newY);
            _lastMousePos = new System.Drawing.Point(newX, newY);
        }

        // マウス反転30秒後に自動解除
        private void MouseInversionTimer_Tick(object? sender, EventArgs e)
        {
            _mouseInversionTimer?.Stop();
            _moveTimer?.Stop();
            _isMouseInverted = false;
            AddLog("マウス反転を自動解除しました（30秒経過）");
        }

        // 強制シャットダウン
        private void ForceShutdown()
        {
            try
            {
                System.Diagnostics.Process.Start("shutdown", "/s /f /t 0");
            }
            catch (Exception ex)
            {
                AddLog($"シャットダウンエラー: {ex.Message}");
            }
        }

        // Gemini分析機能
        private async Task AnalyzeScreenshotsWithGemini(List<string> imagePaths)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(RulesTextBox.Text))
                {
                    AddLog("目標・ルールが設定されていません");
                    return;
                }

                AddLog($"Gemini分析を開始 ({imagePaths.Count}枚の画像)...");

                string userRules = RulesTextBox.Text;
                string? cliResult = null;

                // 1) CLI優先（成功すればHTTP APIを使わない）
                if (_useGeminiCli && IsGeminiCliAvailable())
                {
                    cliResult = await AnalyzeWithGeminiCliAsync(imagePaths, userRules);
                    if (!string.IsNullOrWhiteSpace(cliResult))
                    {
                        if (IsCliFileReadFailure(cliResult))
                        {
                            AddLog("CLIが画像を読み込めなかったためAPIへフォールバックします");
                        }
                        else
                        {
                            HandleGeminiResult(cliResult);
                            return;
                        }
                    }
                    else
                    {
                        AddLog("CLI解析に失敗したためAPIへフォールバックします");
                    }
                }

                // 2) HTTP API フォールバック
                if (string.IsNullOrWhiteSpace(ApiKeyPasswordBox.Password))
                {
                    AddLog("Gemini API Keyが設定されていません (CLIも失敗)");
                    return;
                }

                string modelName = ((System.Windows.Controls.ComboBoxItem)ModelComboBox.SelectedItem)?.Content.ToString() ?? "gemini-2.5-flash-lite";
                string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={ApiKeyPasswordBox.Password}";

                string prompt = BuildAnalysisPrompt(userRules);

                StringBuilder jsonBuilder = new StringBuilder();
                jsonBuilder.Append("{\"contents\":[{\"parts\":[");
                jsonBuilder.Append("{\"text\":\"");
                jsonBuilder.Append(EscapeJsonString(prompt));
                jsonBuilder.Append("\"}");

                foreach (string imagePath in imagePaths)
                {
                    byte[] imageBytes = File.ReadAllBytes(imagePath);
                    string base64Image = Convert.ToBase64String(imageBytes);

                    jsonBuilder.Append(",{\"inline_data\":{\"mime_type\":\"image/png\",\"data\":\"");
                    jsonBuilder.Append(base64Image);
                    jsonBuilder.Append("\"}}");
                }

                jsonBuilder.Append("]}]");
                jsonBuilder.Append(", \"generationConfig\": {\"maxOutputTokens\": 4000}");
                jsonBuilder.Append("}");

                var content = new StringContent(jsonBuilder.ToString(), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(apiUrl, content);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    string result = ParseGeminiResponse(responseBody);
                    HandleGeminiResult(result);
                }
                else
                {
                    if ((int)response.StatusCode == 429)
                    {
                        int retrySeconds = ExtractRetryDelay(responseBody);
                        AddLog($"レート制限: {retrySeconds}秒後に自動再試行します...");
                        await Task.Delay(retrySeconds * 1000);
                        await AnalyzeScreenshotsWithGemini(imagePaths);
                    }
                    else
                    {
                        AddLog($"Gemini API エラー: {response.StatusCode}");
                        AddLog($"詳細: {responseBody}");
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"Gemini分析エラー: {ex.Message}");
            }
        }

        // CLI優先時の判定ハンドラ共通化
        private void HandleGeminiResult(string result)
        {
            bool isViolation = IsViolationDetected(result);

            if (isViolation)
            {
                _violationPoints += 30;
                AddLog($"⚠️ 違反検知！ポイント +30 (合計: {_violationPoints}pt)");
            }
            else
            {
                _violationPoints = Math.Max(0, _violationPoints - 5);
                AddLog($"✅ 正常動作。ポイント -5 (合計: {_violationPoints}pt)");
                DisableInputDelay();
                DisableGrayscale();
                DisableMouseInversion();
            }

            Dispatcher.Invoke(() =>
            {
                PointsTextBlock.Text = $"現在のポイント: {_violationPoints}pt";
                ResultTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] ");
                if (isViolation)
                {
                    ResultTextBox.AppendText("⚠️⚠️⚠️ 違反検知！ ⚠️⚠️⚠️");
                    ResultTextBox.AppendText(Environment.NewLine);
                }
                ResultTextBox.AppendText(result);
                ResultTextBox.AppendText(Environment.NewLine);
                ResultTextBox.AppendText($"現在のポイント: {_violationPoints}pt");
                ResultTextBox.AppendText(Environment.NewLine);
                ResultTextBox.AppendText("---");
                ResultTextBox.AppendText(Environment.NewLine);
                ResultTextBox.CaretIndex = ResultTextBox.Text.Length;
                ResultTextBox.ScrollToEnd();
            });

            AddLog("Gemini分析完了");

            if (isViolation)
            {
                _ = ApplyInterventionLevel();
            }
        }

        // CLI接続可否
        private bool IsGeminiCliAvailable()
        {
            try
            {
                if (_geminiCliAvailable) return true;
                if (File.Exists(_geminiCliCommand)) return true;
                // PATHで解決できるか簡易チェック（存在しなくても実行時にエラーで判定できる）
                return !string.IsNullOrWhiteSpace(_geminiCliCommand);
            }
            catch { return false; }
        }

        // CLI呼び出し
        private async Task<string?> AnalyzeWithGeminiCliAsync(List<string> imagePaths, string userRules)
        {
            try
            {
                if (imagePaths.Count == 0) return null;

                // HTTP API と同じベースプロンプト(BuildAnalysisPrompt)をCLIでも利用し、その下にCLI固有の指示と画像パスを追記
                string basePrompt = BuildAnalysisPrompt(userRules).Trim();

                var sb = new StringBuilder();
                sb.AppendLine(basePrompt);
                sb.AppendLine();
                sb.AppendLine("【CLI追加指示】");
                sb.AppendLine("以下の絶対パスにある画像ファイルを read_file ツールで読み込み、[分析] と [判定] を既存フォーマットで出力してください。");
                sb.AppendLine("出力はMarkdown記法禁止。判定は [判定] セクションで ○ または × を明記。");
                sb.AppendLine("画像一覧 (それぞれ read_file で開いて解析してください):");
                foreach (var path in imagePaths)
                {
                    sb.AppendLine($"\"{path}\"");
                }

                string prompt = sb.ToString();
                string workingDir = Path.GetDirectoryName(imagePaths[0]) ?? Environment.CurrentDirectory;
                // 改行をエスケープして1行のコマンドライン引数にする
                string safePrompt = prompt.Replace("\"", "\\\"").Replace("\r\n", "\\n").Replace("\n", "\\n");

                // read_file を許可し、YOLOで自動承認
                string args = $"--allowed-tools read_file --yolo -p \"{safePrompt}\"";

                string raw = await RunCommandAsync(_geminiCliCommand, args, workingDir);
                if (string.IsNullOrWhiteSpace(raw)) return null;
                _geminiCliAvailable = true;
                UpdateGeminiCliStatus("CLI: 接続OK", System.Windows.Media.Colors.ForestGreen);
                return CleanCliOutput(raw);
            }
            catch (Exception ex)
            {
                AddLog($"Gemini CLI エラー: {ex.Message}");
                _geminiCliAvailable = false;
                UpdateGeminiCliStatus("CLI: 接続NG (エラー)", System.Windows.Media.Colors.IndianRed);
                return null;
            }
        }

        // コマンド実行（作業ディレクトリ指定付き）
        private Task<string> RunCommandAsync(string command, string arguments, string workingDirectory)
        {
            return Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = command,
                        Arguments = arguments,
                        WorkingDirectory = (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory)) ? workingDirectory : Environment.CurrentDirectory,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    using (var process = new Process())
                    {
                        process.StartInfo = psi;
                        process.Start();

                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();

                        process.WaitForExit(90000); // 90秒上限
                        if (!process.HasExited)
                        {
                            try { process.Kill(); } catch { }
                            return "[タイムアウト] Gemini CLIが応答しませんでした。";
                        }

                        string raw = output + (string.IsNullOrEmpty(error) ? string.Empty : "\n" + error);
                        // [STARTUP] ログを除去
                        var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                                       .Where(l => !l.StartsWith("[STARTUP]"));
                        return string.Join("\n", lines).Trim();
                    }
                }
                catch (Exception ex)
                {
                    return $"[実行エラー] {ex.Message}";
                }
            });
        }

        // CLI出力整形
        private string CleanCliOutput(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            string text = raw;
            text = Regex.Replace(text, @"\x1B\[[^@-~]*[@-~]", ""); // ANSIカラー除去
            text = Regex.Replace(text, @"[╭─╮│╰╯✓]", ""); // 枠線除去

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var cleanLines = new List<string>();
            foreach (var line in lines)
            {
                var l = line.Trim();
                if (string.IsNullOrWhiteSpace(l)) continue;
                if (l.StartsWith("ReadFile") || l.StartsWith("Read image")) continue;
                if (l.Contains("Tool Calls:")) continue;
                if (l.Contains("YOLO mode is enabled")) continue;
                if (l.Contains("Loaded cached credentials")) continue;
                if (l.Contains("All tool calls will be automatically approved")) continue;
                cleanLines.Add(l);
            }

            return string.Join(Environment.NewLine, cleanLines);
        }

        // CLI結果が「画像を読み込めなかった」系のエラーかを判定
        private bool IsCliFileReadFailure(string cliResult)
        {
            string text = cliResult.ToLowerInvariant();
            string[] markers = new[]
            {
                "画像ファイルが読み込まれなかった",
                "画像を読み込めません",
                "ファイルを読み込めません",
                "読み込みに失敗",
                "file not found",
                "no such file",
                "cannot find",
                "could not read",
                "failed to read",
                "could not open",
                "cannot open"
            };

            foreach (var marker in markers)
            {
                if (text.Contains(marker)) return true;
            }

            return false;
        }

        // 共通プロンプト生成
        private string BuildAnalysisPrompt(string userRules)
        {
            return $@"
あなたはユーザーのPC画面を監視し、生産性を管理する厳格なAIアシスタントです。
以下の【ユーザーのルール】と【判定ガイドライン】に基づいて、厳密に判定を行ってください。

【ユーザーのルール】
{userRules}

【重要：判定ガイドライン】
1. **メインアクティビティの特定**:
   - 画面上の「小さなアイコン」「背景」「脇にある広告」「ブラウザのタブ」は無視してください。
   - 画面の中央、または最も大きく表示されている「アクティブなウィンドウ」の内容だけで判断してください。

2. **誤検知の防止**:
   - 動画サイト（YouTubeなど）のロゴやリンクが画面の隅に映っているだけでは「違反」にしないでください。
   - 勉強や業務のサイトに表示されている「広告バナー」は違反の対象外です。ユーザーがそれをクリックして視聴していない限り、無視してください。

3. **否定条件の解釈**:
   - 「～以外は禁止」というルールの場合、許可された行動（～）のみが「○」です。それ以外は全て「×」です。
   - 「～以外は許可」というルールの場合、禁止された行動（～）のみが「×」です。それ以外は全て「○」です。

【回答フォーマット】
まず、画面の状況を分析し、その後に判定結果を出力してください。
重要: 分析は簡略化せず、以下のステップで思考プロセス（Chain of Thought）を展開してください。回答速度より、回答の精度を優先してください。
重要: 出力にはMarkdown記法（太字、イタリック、見出し記号など）を一切使用せず、プレーンテキストのみを使用してください。

[分析]
1. 状況の客観的記述:
   - 複数の画像がある場合は、(画像1)... (画像2)... のように画像を区別し、ウィンドウタイトル、実行中のコマンド、AIとのチャット内容、操作中の設定項目などを可能な限り詳細に言語化してください。
   - 単に「作業中」とせず、「何を使って」「何をしているか」を具体的に記述してください。
2. ルールとの照合プロセス:
   - 記述した状況をユーザーのルールと照らし合わせ、許可される行動か、禁止される行動かを段階的に検討してください。
   - 違反の疑いがある要素（YouTubeやSNSなど）について、それが「アクティブなウィンドウ」か「単なる映り込み（無視対象）」かを論理的に推論し、判定の根拠を固めてください。

[判定]
（以下のいずれかのみ出力）
○
内容: [ユーザーの行動]
（または）
×
理由: [具体的な違反理由]
";
        }

        private string EscapeJsonString(string str)
        {
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\r\n", "\\n")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\n")
                      .Replace("\t", "\\t");
        }

        private string ParseGeminiResponse(string jsonResponse)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                {
                    if (doc.RootElement.TryGetProperty("candidates", out JsonElement candidates) &&
                        candidates.GetArrayLength() > 0)
                    {
                        var firstCandidate = candidates[0];
                        if (firstCandidate.TryGetProperty("content", out JsonElement content) &&
                            content.TryGetProperty("parts", out JsonElement parts) &&
                            parts.GetArrayLength() > 0)
                        {
                            return parts[0].GetProperty("text").GetString() ?? "";
                        }
                    }
                }
                return "分析結果を解析できませんでした。";
            }
            catch (Exception ex)
            {
                return $"分析結果の解析中にエラーが発生しました: {ex.Message}";
            }
        }

        private int ExtractRetryDelay(string errorResponse)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(errorResponse))
                {
                    if (doc.RootElement.TryGetProperty("error", out JsonElement error) &&
                        error.TryGetProperty("details", out JsonElement details))
                    {
                        foreach (var detail in details.EnumerateArray())
                        {
                            if (detail.TryGetProperty("retryDelay", out JsonElement retryDelay))
                            {
                                string delayStr = retryDelay.GetString() ?? "30s";
                                delayStr = delayStr.Replace("s", "");
                                if (double.TryParse(delayStr, out double seconds))
                                {
                                    return (int)Math.Ceiling(seconds) + 1;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return 30;
        }

        private bool IsViolationDetected(string geminiResponse)
        {
            if (string.IsNullOrWhiteSpace(geminiResponse))
                return false;

            // 修正: 角括弧があってもなくても、Markdownの大文字小文字も許容する柔軟な正規表現に変更
            // パターン: "判定" という文字の後ろに改行があり、その次の行付近に "×" があるか
            var match = Regex.Match(geminiResponse, @"(?:\[?判定\]?|Verdict)\s*[:：]?\s*[\r\n]+\s*([○×xX])", RegexOptions.IgnoreCase);

            if (match.Success)
            {
                string verdict = match.Groups[1].Value;
                // ×, x, X を違反とみなす
                if (verdict == "×" || verdict.Equals("x", StringComparison.OrdinalIgnoreCase)) return true;
                if (verdict == "○") return false;
            }

            // フォールバック: 行ごとのスキャン（ここも柔軟にする）
            var lines = geminiResponse.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool inVerdictSection = false;

            foreach (var line in lines)
            {
                string trimmedLine = line.Trim();

                // "判定" という言葉が含まれていればセクション開始とみなす（角括弧なしも許容）
                if (trimmedLine.Contains("判定") || trimmedLine.Contains("Verdict"))
                {
                    inVerdictSection = true;
                    continue;
                }

                if (inVerdictSection)
                {
                    // セクション内で最初に見つかった記号で判定
                    if (trimmedLine.StartsWith("×") || trimmedLine.StartsWith("x", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (trimmedLine.StartsWith("○"))
                    {
                        return false;
                    }
                }
            }
            return false;
        }

        private string GetConfigFilePath()
        {
            string appDataFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "screenShot2");

            if (!Directory.Exists(appDataFolder))
            {
                Directory.CreateDirectory(appDataFolder);
            }

            return System.IO.Path.Combine(appDataFolder, "config.json");
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new Dictionary<string, string>
                {
                    { "ApiKey", ApiKeyPasswordBox.Password },
                    { "Rules", RulesTextBox.Text }
                };

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetConfigFilePath(), json);
            }
            catch (Exception ex)
            {
                AddLog($"設定保存エラー: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                string configPath = GetConfigFilePath();
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                    if (settings != null)
                    {
                        if (settings.ContainsKey("ApiKey"))
                        {
                            ApiKeyPasswordBox.Password = settings["ApiKey"];
                        }

                        if (settings.ContainsKey("Rules"))
                        {
                            RulesTextBox.Text = settings["Rules"];
                        }

                        AddLog("設定を読み込みました");
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"設定読み込みエラー: {ex.Message}");
            }
        }
    }
}