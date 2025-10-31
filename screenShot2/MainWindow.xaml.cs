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

namespace screenShot2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DispatcherTimer? _timer;
        private string _saveFolderPath = string.Empty;
        private int _screenshotCount = 0;
        private static readonly HttpClient _httpClient = new HttpClient();
        private List<string> _currentScreenshotPaths = new List<string>();
        
        // 画面ロック用のWin32 API
        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();

        public MainWindow()
        {
            InitializeComponent();
            InitializeApp();
        }

        private void InitializeApp()
        {
            // デフォルトの保存先をドキュメントフォルダに設定
            _saveFolderPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Screenshots");
            FolderPathTextBox.Text = _saveFolderPath;

            // モニター情報を更新
            UpdateMonitorInfo();
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
            if (!int.TryParse(IntervalTextBox.Text, out int interval) || interval <= 0)
            {
                System.Windows.MessageBox.Show("有効な秒数を入力してください（1以上の整数）", "入力エラー", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
            IntervalTextBox.IsEnabled = false;
            BrowseButton.IsEnabled = false;

            StatusTextBlock.Text = "撮影中...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            AddLog($"撮影開始 - 間隔: {interval}秒");

            // タイマー設定
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(interval)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // 即座に最初のスクリーンショットを撮影
            CaptureAllScreens();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopCapture();
        }

        private void StopCapture()
        {
            _timer?.Stop();
            _timer = null;

            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            IntervalTextBox.IsEnabled = true;
            BrowseButton.IsEnabled = true;

            StatusTextBlock.Text = "停止";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Gray);
            AddLog($"撮影停止 - 合計 {_screenshotCount} 枚撮影");
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            CaptureAllScreens();
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
                
                // Gemini分析が有効な場合は送信
                if (EnableGeminiCheckBox.IsChecked == true && _currentScreenshotPaths.Count > 0)
                {
                    await AnalyzeScreenshotsWithGemini(_currentScreenshotPaths);
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
                    // bounds.Location は画面の左上座標、Size は幅と高さ
                    graphics.CopyFromScreen(
                        bounds.Left,      // ソースX座標
                        bounds.Top,       // ソースY座標
                        0,                // デスティネーションX座標
                        0,                // デスティネーションY座標
                        new System.Drawing.Size(bounds.Width, bounds.Height),  // コピーするサイズ
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
                LogTextBlock.Text += logMessage;
                
                // ログが長くなりすぎないように制限
                if (LogTextBlock.Text.Length > 5000)
                {
                    var lines = LogTextBlock.Text.Split('\n');
                    LogTextBlock.Text = string.Join('\n', lines.Skip(lines.Length / 2));
                }
            });
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _timer?.Stop();
            base.OnClosing(e);
        }
        
        // Gemini分析機能
        private async Task AnalyzeScreenshotsWithGemini(List<string> imagePaths)
        {
            try
            {
                // API Keyのバリデーション
                if (string.IsNullOrWhiteSpace(ApiKeyPasswordBox.Password))
                {
                    AddLog("Gemini API Keyが設定されていません");
                    return;
                }
                
                if (string.IsNullOrWhiteSpace(RulesTextBox.Text))
                {
                    AddLog("目標・ルールが設定されていません");
                    return;
                }
                
                AddLog($"Gemini分析を開始 ({imagePaths.Count}枚の画像)...");
                
                // 選択されたモデル名を取得
                string modelName = ((System.Windows.Controls.ComboBoxItem)ModelComboBox.SelectedItem).Content.ToString() ?? "gemini-2.5-flash";
                
                // Gemini APIのエンドポイント
                string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={ApiKeyPasswordBox.Password}";
                
                // ユーザーのルールを取得
                string userRules = RulesTextBox.Text;
                
                // プロンプトを作成
                string prompt = $@"以下のルールに基づいて、これらのWindowsの画面を分析してください。

【ルール】
{userRules}

【指示】
1. 全ての画像を見て、ユーザーが何をしているか分析してください
2. ユーザーの行動がルールに違反している場合は、以下の形式で回答してください：
   ×
   理由: [具体的な違反内容]
3. ルールに違反していない（正しい行動をしている）場合は、以下の形式で回答してください：
   ○
   内容: [何をしているか簡潔に]

回答:";
                
                // JSON リクエストボディを作成
                StringBuilder jsonBuilder = new StringBuilder();
                jsonBuilder.Append("{\"contents\":[{\"parts\":[");
                
                // テキストプロンプトを追加
                jsonBuilder.Append("{\"text\":\"");
                jsonBuilder.Append(EscapeJsonString(prompt));
                jsonBuilder.Append("\"}");
                
                // 各画像をBase64エンコードして追加
                foreach (string imagePath in imagePaths)
                {
                    byte[] imageBytes = File.ReadAllBytes(imagePath);
                    string base64Image = Convert.ToBase64String(imageBytes);
                    
                    jsonBuilder.Append(",{\"inline_data\":{\"mime_type\":\"image/png\",\"data\":\"");
                    jsonBuilder.Append(base64Image);
                    jsonBuilder.Append("\"}}");
                }
                
                jsonBuilder.Append("]}]}");
                
                var content = new StringContent(jsonBuilder.ToString(), Encoding.UTF8, "application/json");
                
                // API呼び出し
                var response = await _httpClient.PostAsync(apiUrl, content);
                string responseBody = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    // レスポンスから結果を抽出
                    string result = ParseGeminiResponse(responseBody);
                    
                    // 違反検知チェック
                    bool isViolation = IsViolationDetected(result);
                    
                    // 結果を表示
                    Dispatcher.Invoke(() =>
                    {
                        ResultTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] ");
                        
                        // 違反時は目立つように表示
                        if (isViolation)
                        {
                            ResultTextBox.AppendText("⚠️⚠️⚠️ 違反検知！ ⚠️⚠️⚠️");
                            ResultTextBox.AppendText(Environment.NewLine);
                        }
                        
                        ResultTextBox.AppendText(result);
                        ResultTextBox.AppendText(Environment.NewLine);
                        ResultTextBox.AppendText("---");
                        ResultTextBox.AppendText(Environment.NewLine);
                        
                        // 最新のテキストが見えるように自動スクロール
                        ResultTextBox.CaretIndex = ResultTextBox.Text.Length;
                        ResultTextBox.ScrollToEnd();
                    });
                    
                    AddLog("Gemini分析完了");
                    
                    // ⭐ 違反検知時に画面ロック（3秒後に実行して結果を見る時間を確保）
                    if (EnableLockCheckBox.IsChecked == true && isViolation)
                    {
                        AddLog("⚠️ 違反検知！3秒後に画面をロックします...");
                        Dispatcher.Invoke(() =>
                        {
                            ResultTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 🔒 3秒後に画面ロック実行...");
                            ResultTextBox.AppendText(Environment.NewLine);
                            
                            // 最新のテキストが見えるように自動スクロール
                            ResultTextBox.CaretIndex = ResultTextBox.Text.Length;
                            ResultTextBox.ScrollToEnd();
                        });
                        
                        // 3秒待ってから画面ロック
                        await Task.Delay(3000);
                        LockWorkStation();
                        AddLog("画面ロック実行完了");
                    }
                }
                else
                {
                    // レート制限エラー(429)の場合
                    if ((int)response.StatusCode == 429)
                    {
                        int retrySeconds = ExtractRetryDelay(responseBody);
                        AddLog($"レート制限: {retrySeconds}秒後に自動再試行します...");
                        
                        await Task.Delay(retrySeconds * 1000);
                        // 再帰呼び出しで再試行
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
                // 簡易的なJSON解析（candidates[0].content.parts[0].textを抽出）
                int textIndex = jsonResponse.IndexOf("\"text\":");
                if (textIndex != -1)
                {
                    int startIndex = jsonResponse.IndexOf("\"", textIndex + 7) + 1;
                    int endIndex = jsonResponse.IndexOf("\"", startIndex);
                    
                    if (startIndex > 0 && endIndex > startIndex)
                    {
                        string result = jsonResponse.Substring(startIndex, endIndex - startIndex);
                        // エスケープシーケンスを処理
                        result = result.Replace("\\n", "\n").Replace("\\\"", "\"");
                        return result;
                    }
                }
                return "分析結果を解析できませんでした。";
            }
            catch
            {
                return "分析結果の解析中にエラーが発生しました。";
            }
        }
        
        private int ExtractRetryDelay(string errorResponse)
        {
            try
            {
                // "retryDelay": "24s" のような形式から秒数を抽出
                int retryIndex = errorResponse.IndexOf("\"retryDelay\":");
                if (retryIndex != -1)
                {
                    int startIndex = errorResponse.IndexOf("\"", retryIndex + 13) + 1;
                    int endIndex = errorResponse.IndexOf("\"", startIndex);
                    
                    if (startIndex > 0 && endIndex > startIndex)
                    {
                        string delayStr = errorResponse.Substring(startIndex, endIndex - startIndex);
                        // "24s" や "24.504554452s" から数値を抽出
                        delayStr = delayStr.Replace("s", "");
                        if (double.TryParse(delayStr, out double seconds))
                        {
                            return (int)Math.Ceiling(seconds) + 1; // 切り上げ+1秒の余裕
                        }
                    }
                }
            }
            catch
            {
                // エラー時はデフォルトで30秒待機
            }
            return 30; // デフォルト30秒
        }
        
        // 違反検知ロジック
        private bool IsViolationDetected(string geminiResponse)
        {
            if (string.IsNullOrWhiteSpace(geminiResponse))
                return false;
            
            string response = geminiResponse.Trim();
            
            // まず最初に「○」で始まる場合は違反なし
            if (response.StartsWith("○"))
                return false;
            
            // 「×」で始まる場合は違反
            if (response.StartsWith("×"))
                return true;
            
            // 「○」が含まれていて、かつ「内容:」も含まれている場合は違反なし
            if (response.Contains("○") && response.Contains("内容:"))
                return false;
            
            // 違反を示すキーワード（ただし「○」がない場合のみチェック）
            if (!response.Contains("○"))
            {
                string[] violationKeywords = { "違反", "ルール違反", "問題あり", "不適切" };
                foreach (string keyword in violationKeywords)
                {
                    if (response.Contains(keyword))
                        return true;
                }
            }
            
            return false;
        }
    }
}