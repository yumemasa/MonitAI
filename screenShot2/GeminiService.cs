using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace screenShot2
{
    public class GeminiAnalysisResult
    {
        public string RawText { get; set; } = "";
        public bool IsViolation { get; set; }
        public string Source { get; set; } = "Unknown"; // "CLI" or "API"
    }

    public class GeminiService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        
        // CLIコマンドのパス（デフォルトはPATH上のgemini、必要に応じて設定可能）
        public string GeminiCliCommand { get; set; } = "gemini";
        public bool UseGeminiCli { get; set; } = true;

        public async Task<GeminiAnalysisResult> AnalyzeAsync(List<string> imagePaths, string userRules, string apiKey, string modelName)
        {
            var result = new GeminiAnalysisResult();

            if (imagePaths == null || imagePaths.Count == 0)
            {
                result.RawText = "画像がありません。";
                return result;
            }

            // 1. CLIでの実行を試みる
            if (UseGeminiCli)
            {
                string? cliOutput = await AnalyzeWithCliAsync(imagePaths, userRules);
                if (!string.IsNullOrWhiteSpace(cliOutput) && !IsCliFileReadFailure(cliOutput))
                {
                    result.RawText = cliOutput;
                    result.IsViolation = IsViolationDetected(cliOutput);
                    result.Source = "CLI";
                    return result;
                }
            }

            // 2. APIへのフォールバック
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                result.RawText = "APIキーが設定されておらず、CLI実行も失敗しました。";
                return result;
            }

            string apiOutput = await AnalyzeWithApiAsync(imagePaths, userRules, apiKey, modelName);
            result.RawText = apiOutput;
            result.IsViolation = IsViolationDetected(apiOutput);
            result.Source = "API";
            
            return result;
        }

        public async Task<bool> CheckCliConnectionAsync()
        {
            try
            {
                string output = await RunCommandAsync(GeminiCliCommand, "--version", Environment.CurrentDirectory);
                return !string.IsNullOrWhiteSpace(output) && 
                       !output.Contains("error", StringComparison.OrdinalIgnoreCase) && 
                       !output.Contains("not found", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private async Task<string?> AnalyzeWithCliAsync(List<string> imagePaths, string userRules)
        {
            try
            {
                string basePrompt = BuildAnalysisPrompt(userRules).Trim();
                var sb = new StringBuilder();
                sb.AppendLine(basePrompt);
                sb.AppendLine("\n【CLI追加指示】");
                sb.AppendLine("以下の画像を read_file ツールで読み込み、[分析] と [判定] を出力してください。");
                foreach (var path in imagePaths)
                {
                    sb.AppendLine($"\"{path}\"");
                }

                string prompt = sb.ToString();
                string workingDir = Path.GetDirectoryName(imagePaths[0]) ?? Environment.CurrentDirectory;
                string safePrompt = prompt.Replace("\"", "\\\"").Replace("\r\n", "\\n").Replace("\n", "\\n");
                string args = $"--allowed-tools read_file --yolo -p \"{safePrompt}\"";

                string raw = await RunCommandAsync(GeminiCliCommand, args, workingDir);
                return CleanCliOutput(raw);
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> AnalyzeWithApiAsync(List<string> imagePaths, string userRules, string apiKey, string modelName)
        {
            try
            {
                string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
                string prompt = BuildAnalysisPrompt(userRules);

                var jsonBuilder = new StringBuilder();
                jsonBuilder.Append("{\"contents\":[{\"parts\":[");
                jsonBuilder.Append("{\"text\":\"").Append(EscapeJsonString(prompt)).Append("\"}");

                foreach (string imagePath in imagePaths)
                {
                    byte[] imageBytes = File.ReadAllBytes(imagePath);
                    string base64Image = Convert.ToBase64String(imageBytes);
                    jsonBuilder.Append(",{\"inline_data\":{\"mime_type\":\"image/png\",\"data\":\"").Append(base64Image).Append("\"}}");
                }

                jsonBuilder.Append("]}]");
                jsonBuilder.Append(", \"generationConfig\": {\"maxOutputTokens\": 4000}");
                jsonBuilder.Append("}");

                var content = new StringContent(jsonBuilder.ToString(), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(apiUrl, content);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return ParseGeminiResponse(responseBody);
                }
                else
                {
                    return $"API Error: {response.StatusCode} - {responseBody}";
                }
            }
            catch (Exception ex)
            {
                return $"Exception: {ex.Message}";
            }
        }

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
                        WorkingDirectory = workingDirectory,
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
                        process.WaitForExit(90000);
                        if (!process.HasExited) { process.Kill(); return "Timeout"; }
                        
                        string raw = output + (string.IsNullOrEmpty(error) ? "" : "\n" + error);
                        var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                                       .Where(l => !l.StartsWith("[STARTUP]"));
                        return string.Join("\n", lines).Trim();
                    }
                }
                catch (Exception ex) { return $"Error: {ex.Message}"; }
            });
        }

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

        private bool IsViolationDetected(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var match = Regex.Match(text, @"(?:\[?判定\]?|Verdict)\s*[:：]?\s*[\r\n]+\s*([○×xX])", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string v = match.Groups[1].Value;
                return v == "×" || v.Equals("x", StringComparison.OrdinalIgnoreCase);
            }
            
            // フォールバック検索
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool inSection = false;
            foreach (var line in lines)
            {
                if (line.Contains("判定") || line.Contains("Verdict")) { inSection = true; continue; }
                if (inSection)
                {
                    if (line.Trim().StartsWith("×") || line.Trim().StartsWith("x", StringComparison.OrdinalIgnoreCase)) return true;
                    if (line.Trim().StartsWith("○")) return false;
                }
            }
            return false;
        }

        private string CleanCliOutput(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string text = Regex.Replace(raw, @"\x1B\[[^@-~]*[@-~]", ""); // ANSIカラー除去
            text = Regex.Replace(text, @"[╭─╮│╰╯✓]", ""); // 枠線除去
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                            .Where(l => !l.StartsWith("ReadFile") && !l.Contains("Tool Calls") && !l.Contains("YOLO mode"));
            return string.Join(Environment.NewLine, lines).Trim();
        }

        private bool IsCliFileReadFailure(string text)
        {
            string t = text.ToLowerInvariant();
            return t.Contains("画像ファイルが読み込まれなかった") || t.Contains("file not found") || t.Contains("cannot open");
        }

        private string EscapeJsonString(string str)
        {
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        private string ParseGeminiResponse(string json)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
                }
            }
            catch { return "Parse Error"; }
        }
    }
}
