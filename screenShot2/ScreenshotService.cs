using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms; 

namespace screenShot2
{
    public class ScreenshotService
    {
        public string DefaultSaveFolderPath { get; private set; }

        public ScreenshotService()
        {
            // 環境依存のパスを動的に取得 (MyPictures/capture)
            DefaultSaveFolderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "capture");
        }

        /// <summary>
        /// 全画面をキャプチャし、保存されたファイルパスのリストを返します。
        /// </summary>
        public List<string> CaptureAllScreens(string? saveFolderPath = null)
        {
            var savedPaths = new List<string>();
            string targetFolder = string.IsNullOrWhiteSpace(saveFolderPath) ? DefaultSaveFolderPath : saveFolderPath;

            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            var screens = Screen.AllScreens;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                string filePath = CaptureScreen(screen, i, timestamp, targetFolder);
                if (!string.IsNullOrEmpty(filePath))
                {
                    savedPaths.Add(filePath);
                }
            }

            return savedPaths;
        }

        private string CaptureScreen(Screen screen, int screenIndex, string timestamp, string saveFolder)
        {
            var bounds = screen.Bounds;

            using (var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                    graphics.CopyFromScreen(
                        bounds.Left,
                        bounds.Top,
                        0,
                        0,
                        new Size(bounds.Width, bounds.Height),
                        CopyPixelOperation.SourceCopy);
                }

                string monitorName = screen.Primary ? "Main" : $"Monitor{screenIndex + 1}";
                string fileName = $"Screenshot_{timestamp}_{monitorName}.png";
                string filePath = Path.Combine(saveFolder, fileName);

                bitmap.Save(filePath, ImageFormat.Png);
                return filePath;
            }
        }
        
        public void DeleteFiles(IEnumerable<string> filePaths)
        {
            foreach (var path in filePaths)
            {
                if (File.Exists(path))
                {
                    try { File.Delete(path); } catch { /* 無視 */ }
                }
            }
        }
    }
}