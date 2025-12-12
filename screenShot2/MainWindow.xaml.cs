using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace screenShot2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // サービス群
        private readonly ScreenshotService _screenshotService;
        private readonly GeminiService _geminiService;
        private readonly InterventionService _interventionService;

        // 状態管理
        private bool _isCapturing = false;
        private int _screenshotCount = 0;
        private int _violationPoints = 0;
        private string _saveFolderPath = string.Empty;
        private Random _random = new Random();
        private System.Windows.Forms.NotifyIcon? _notifyIcon;

        public MainWindow()
        {
            InitializeComponent();

            // サービスの初期化
            _screenshotService = new ScreenshotService();
            _geminiService = new GeminiService();
            // 環境に合わせてCLIのパスを設定
            _geminiService.GeminiCliCommand = @"C:\nvm4w\nodejs\gemini.cmd"; 
            _interventionService = new InterventionService();

            // イベント購読
            _interventionService.OnLog += AddLog;
            _interventionService.OnNotification += ShowNotification;

            InitializeApp();
        }

        private void InitializeApp()
        {
            // デフォルトの保存先
            _saveFolderPath = _screenshotService.DefaultSaveFolderPath;
            FolderPathTextBox.Text = _saveFolderPath;

            // モニター情報を更新
            UpdateMonitorInfo();

            // 設定ファイルからAPIキーを読み込む
            LoadSettings();

            // 通知アイコンの初期化
            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Warning,
                Visible = true,
                Text = "ScreenShot2 Monitor"
            };

            // Gemini CLI 接続チェック
            _ = CheckGeminiCliConnectionAsync();
        }

        private void UpdateMonitorInfo()
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            MonitorInfoTextBlock.Text = $"検出されたモニター: {screens.Length}";
            AddLog($"モニター数: {screens.Length}");
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
                System.Windows.MessageBox.Show("保存先フォルダを選択してください", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            _ = StartCaptureLoop();
        }

        private async Task StartCaptureLoop()
        {
            const int cycleMs = 20000; // サイクル時間

            while (_isCapturing)
            {
                try
                {
                    int delay = _random.Next(1000, cycleMs - 1000);
                    AddLog($"次の撮影まで待機: {delay / 1000.0:F1}秒");

                    await Task.Delay(delay);
                    if (!_isCapturing) break;

                    await PerformCaptureAndAnalysis();

                    int remaining = cycleMs - delay;
                    if (remaining > 0) await Task.Delay(remaining);
                }
                catch (Exception ex)
                {
                    AddLog($"ループエラー: {ex.Message}");
                    await Task.Delay(5000);
                }
            }
        }

        private async Task PerformCaptureAndAnalysis()
        {
            // 1. 撮影
            var files = _screenshotService.CaptureAllScreens(_saveFolderPath);
            if (files.Count == 0) return;

            _screenshotCount += files.Count;
            Dispatcher.Invoke(() => StatusTextBlock.Text = $"撮影中... (合計: {_screenshotCount}枚)");
            AddLog($"撮影完了: {files.Count}枚");

            // 2. 分析
            string rules = "";
            string apiKey = "";
            string modelName = "";
            Dispatcher.Invoke(() =>
            {
                rules = RulesTextBox.Text;
                apiKey = ApiKeyPasswordBox.Password;
                modelName = ((ComboBoxItem)ModelComboBox.SelectedItem)?.Content.ToString() ?? "gemini-2.5-flash-lite";
            });

            if (string.IsNullOrWhiteSpace(rules))
            {
                AddLog("ルール未設定のため分析をスキップ");
                _screenshotService.DeleteFiles(files);
                return;
            }

            AddLog("Gemini分析中...");
            var result = await _geminiService.AnalyzeAsync(files, rules, apiKey, modelName);

            // 3. 結果処理
            HandleAnalysisResult(result);

            // 4. ファイル削除
            _screenshotService.DeleteFiles(files);
        }

        private void HandleAnalysisResult(GeminiAnalysisResult result)
        {
            if (result.IsViolation)
            {
                _violationPoints += 30;
                AddLog($"⚠️ 違反検知！ポイント +30 (合計: {_violationPoints}pt)");
            }
            else
            {
                _violationPoints = Math.Max(0, _violationPoints - 5);
                AddLog($"✅ 正常動作。ポイント -5 (合計: {_violationPoints}pt)");
                // 正常時は介入リセット
                if (_violationPoints == 0) _interventionService.ResetAllInterventions();
            }

            // UI更新
            Dispatcher.Invoke(() =>
            {
                PointsTextBlock.Text = $"現在のポイント: {_violationPoints}pt";
                ResultTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {(result.IsViolation ? "⚠️違反" : "✅正常")} ({result.Source})\n");
                ResultTextBox.AppendText(result.RawText + "\n---\n");
                ResultTextBox.ScrollToEnd();
            });

            // 介入実行
            string goalSummary = "";
            Dispatcher.Invoke(() => goalSummary = RulesTextBox.Text.Split('\n').FirstOrDefault() ?? "目標");
            _ = _interventionService.ApplyLevelAsync(_violationPoints, goalSummary);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _isCapturing = false;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            BrowseButton.IsEnabled = true;
            StatusTextBlock.Text = "停止";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Gray);
            AddLog("撮影停止");
        }

        // --- ログ・通知・設定 ---

        private void AddLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogTextBlock.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                LogTextBlock.ScrollToEnd();
                if (LogTextBlock.Text.Length > 5000)
                    LogTextBlock.Text = LogTextBlock.Text.Substring(LogTextBlock.Text.Length / 2);
            });
        }

        private void ShowNotification(string message, string title)
        {
            _notifyIcon?.ShowBalloonTip(3000, title, message, System.Windows.Forms.ToolTipIcon.Warning);
        }

        private async void CheckGeminiCliButton_Click(object sender, RoutedEventArgs e)
        {
            await CheckGeminiCliConnectionAsync();
        }

        private async Task CheckGeminiCliConnectionAsync()
        {
            Dispatcher.Invoke(() => GeminiCliStatusTextBlock.Text = "CLI: 確認中...");
            bool ok = await _geminiService.CheckCliConnectionAsync();
            Dispatcher.Invoke(() =>
            {
                GeminiCliStatusTextBlock.Text = ok ? "CLI: 接続OK" : "CLI: 接続NG";
                GeminiCliStatusTextBlock.Foreground = new SolidColorBrush(ok ? Colors.ForestGreen : Colors.IndianRed);
            });
        }

        private string GetConfigFilePath()
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "screenShot2");
            if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
            return Path.Combine(appData, "config.json");
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
                File.WriteAllText(GetConfigFilePath(), JsonSerializer.Serialize(settings));
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                string path = GetConfigFilePath();
                if (File.Exists(path))
                {
                    var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                    if (settings != null)
                    {
                        if (settings.ContainsKey("ApiKey")) ApiKeyPasswordBox.Password = settings["ApiKey"];
                        if (settings.ContainsKey("Rules")) RulesTextBox.Text = settings["Rules"];
                    }
                }
            }
            catch { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _isCapturing = false;
            _interventionService.Dispose();
            _notifyIcon?.Dispose();
            SaveSettings();
            base.OnClosing(e);
        }
    }
}
