using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms; // Timer, Keys, SendKeys
using NAudio.CoreAudioApi;

namespace screenShot2
{
    public class InterventionService : IDisposable
    {
        // 状態フラグ
        private bool _isDelayEnabled = false;
        private bool _isGrayscaleEnabled = false;
        private bool _isMouseInverted = false;
        private bool _isSendingKey = false;

        // タイマーとフック
        private System.Windows.Forms.Timer? _moveTimer;
        private System.Windows.Forms.Timer? _mouseInversionTimer;
        private Point _lastMousePos;
        private IntPtr _keyboardHookID = IntPtr.Zero;
        private NativeMethods.LowLevelKeyboardProc? _keyboardProc; // ガベージコレクション防止のため保持

        // イベント（UIへの通知用）
        public event Action<string>? OnLog;
        public event Action<string, string>? OnNotification; // message, title

        public InterventionService()
        {
            InitializeTimers();
            _keyboardProc = KeyboardHookCallback;
        }

        private void InitializeTimers()
        {
            _moveTimer = new System.Windows.Forms.Timer { Interval = 2 };
            _moveTimer.Tick += MoveTimer_Tick;

            _mouseInversionTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            _mouseInversionTimer.Tick += MouseInversionTimer_Tick;
        }

        /// <summary>
        /// ポイントに基づいて介入レベルを適用します。
        /// </summary>
        public async Task ApplyLevelAsync(int points, string goalSummary)
        {
            string message = "";

            // レベル1: 通知
            if (points >= 1 && points <= 30)
            {
                message = "📢 レベル1: 警告通知";
                OnNotification?.Invoke($"あなたの目標は「{goalSummary}」です。やるべきことに戻りましょう。", "警告");
            }
            else if (points <= 0)
            {
                ResetAllInterventions();
                return;
            }
            // レベル2: 入力遅延
            else if (points <= 60)
            {
                message = "⏱️ レベル2: 入力遅延開始";
                EnableInputDelay();
                OnNotification?.Invoke($"レベル2: 入力遅延が発生しています。", "警告");
            }
            // レベル3: グレースケール
            else if (points <= 100)
            {
                message = "🎨 レベル3: グレースケール適用";
                EnableInputDelay();
                ApplyGrayscale();
                OnNotification?.Invoke($"レベル3: グレースケール適用中。", "警告");
            }
            // レベル4: マウス反転
            else if (points <= 150)
            {
                message = "🖱️ レベル4: マウス反転開始";
                EnableInputDelay();
                ApplyGrayscale();
                EnableMouseInversion();
                OnNotification?.Invoke($"レベル4: マウス反転中。", "警告");
            }
            // レベル5: ビープ音
            else if (points <= 200)
            {
                message = "🔔 レベル5: ビープ音";
                EnableInputDelay();
                ApplyGrayscale();
                EnableMouseInversion();
                await PlayForcedAlertAsync();
            }
            // レベル6: 画面ロック
            else if (points <= 250)
            {
                message = "🔒 レベル6: 画面ロック実行";
                OnLog?.Invoke("⚠️ ポイント上限！3秒後に画面をロックします...");
                await Task.Delay(3000);
                NativeMethods.LockWorkStation();
                return;
            }
            // レベル7: シャットダウン
            else
            {
                message = "💻 レベル7: 強制シャットダウン";
                OnLog?.Invoke("⚠️⚠️⚠️ 最終警告！5秒後にシャットダウンします！");
                await Task.Delay(5000);
                Process.Start("shutdown", "/s /f /t 0");
                return;
            }

            OnLog?.Invoke(message);
        }

        public void ResetAllInterventions()
        {
            DisableInputDelay();
            DisableGrayscale();
            DisableMouseInversion();
        }

        // --- 個別の介入機能 ---

        private void EnableInputDelay()
        {
            if (_isDelayEnabled) return;
            try
            {
                using (var curProcess = Process.GetCurrentProcess())
                using (var curModule = curProcess.MainModule)
                {
                    if (curModule != null)
                    {
                        _keyboardHookID = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc!,
                            NativeMethods.GetModuleHandle(curModule.ModuleName), 0);
                        if (_keyboardHookID != IntPtr.Zero)
                        {
                            _isDelayEnabled = true;
                            OnLog?.Invoke("入力遅延を有効化しました");
                        }
                    }
                }
            }
            catch (Exception ex) { OnLog?.Invoke($"入力遅延エラー: {ex.Message}"); }
        }

        private void DisableInputDelay()
        {
            if (!_isDelayEnabled) return;
            if (_keyboardHookID != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHookID);
                _keyboardHookID = IntPtr.Zero;
                _isDelayEnabled = false;
                OnLog?.Invoke("入力遅延を解除しました");
            }
        }

        private void ApplyGrayscale()
        {
            if (_isGrayscaleEnabled) return;
            try
            {
                if (!NativeMethods.MagInitialize()) return;
                var matrix = new NativeMethods.MAGCOLOREFFECT
                {
                    transform = new float[] {
                        0.3f,0.3f,0.3f,0,0,
                        0.6f,0.6f,0.6f,0,0,
                        0.1f,0.1f,0.1f,0,0,
                        0,0,0,1,0,
                        0,0,0,0,1
                    }
                };
                if (NativeMethods.MagSetFullscreenColorEffect(ref matrix))
                {
                    _isGrayscaleEnabled = true;
                    OnLog?.Invoke("グレースケールを適用しました");
                }
            }
            catch (Exception ex) { OnLog?.Invoke($"グレースケールエラー: {ex.Message}"); }
        }

        private void DisableGrayscale()
        {
            if (!_isGrayscaleEnabled) return;
            try
            {
                NativeMethods.MagUninitialize();
                _isGrayscaleEnabled = false;
                OnLog?.Invoke("グレースケールを解除しました");
            }
            catch { }
        }

        private void EnableMouseInversion()
        {
            if (_isMouseInverted) return;
            _isMouseInverted = true;
            NativeMethods.GetCursorPos(out var p);
            _lastMousePos = new Point(p.X, p.Y);
            _moveTimer?.Start();
            _mouseInversionTimer?.Start();
            OnLog?.Invoke("マウス反転を開始しました");
        }

        private void DisableMouseInversion()
        {
            if (!_isMouseInverted) return;
            _moveTimer?.Stop();
            _mouseInversionTimer?.Stop();
            _isMouseInverted = false;
            OnLog?.Invoke("マウス反転を解除しました");
        }

        // --- イベントハンドラ & コールバック ---

        private void MoveTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isMouseInverted) return;
            NativeMethods.GetCursorPos(out var current);
            int dx = current.X - _lastMousePos.X;
            int dy = current.Y - _lastMousePos.Y;
            int newX = current.X - (int)(dx * 1.5);
            int newY = current.Y - (int)(dy * 1.5);
            NativeMethods.SetCursorPos(newX, newY);
            _lastMousePos = new Point(newX, newY);
        }

        private void MouseInversionTimer_Tick(object? sender, EventArgs e)
        {
            DisableMouseInversion();
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_KEYDOWN && _isDelayEnabled && !_isSendingKey)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;
                bool isAlpha = (vkCode >= (int)Keys.A && vkCode <= (int)Keys.Z);
                bool isNumber = (vkCode >= (int)Keys.D0 && vkCode <= (int)Keys.D9);

                if (isAlpha || isNumber)
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Thread.Sleep(1000);
                        try
                        {
                            _isSendingKey = true;
                            string sendStr = isAlpha ? key.ToString().ToLower() : key.ToString().Replace("D", "");
                            SendKeys.SendWait(sendStr);
                        }
                        catch { }
                        finally { _isSendingKey = false; }
                    });
                    return (IntPtr)1; // キー入力をキャンセル
                }
            }
            return NativeMethods.CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
        }

        private async Task PlayForcedAlertAsync()
        {
            MMDevice? device = null;
            float originalVolume = 0;
            bool originalMute = false;
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
                    device.AudioEndpointVolume.Mute = false;
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = 0.8f;
                }
                await Task.Run(() => {
                    for (int i = 0; i < 3; i++) { Console.Beep(2000, 200); Console.Beep(1000, 200); }
                });
            }
            catch { }
            finally
            {
                if (device != null)
                {
                    try { device.AudioEndpointVolume.Mute = originalMute; device.AudioEndpointVolume.MasterVolumeLevelScalar = originalVolume; } catch { }
                }
            }
        }

        public void Dispose()
        {
            ResetAllInterventions();
            _moveTimer?.Dispose();
            _mouseInversionTimer?.Dispose();
        }
    }
}