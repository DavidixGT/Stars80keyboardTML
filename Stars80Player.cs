using System;
using System.Threading.Tasks;
using Terraria;
using Terraria.ModLoader;

namespace stars80qmkmod
{
    public class Stars80Player : ModPlayer
    {
        private const int MaxFlashDuration = 60; 
        private int _flashTimer = 0;
        private bool _isFadingActive = false;

        private static readonly object ColorLock = new object();
        private static byte _targetR, _targetG, _targetB;
        private static bool _isWorkerRunning = false;

        public override void OnHurt(Player.HurtInfo info)
        {
            if (Main.dedServ || !OperatingSystem.IsWindows()) return;

            _flashTimer = MaxFlashDuration;
            _isFadingActive = true;

            UpdateSharedColor(255, 0, 0);
            EnsureWorkerIsRunning();
        }

        public override void PostUpdate()
        {
            if (Main.dedServ || Main.gameMenu || !Player.active || !_isFadingActive) 
                return;

            _flashTimer--;

            if (_flashTimer > 0)
            {
                double fadeRatio = (double)_flashTimer / MaxFlashDuration;
                byte currentRed = (byte)(fadeRatio * 255);
                UpdateSharedColor(currentRed, 0, 0);
            }
            else
            {
                UpdateSharedColor(0, 0, 0);
                _isFadingActive = false;
            }
        }

        private static void UpdateSharedColor(byte r, byte g, byte b)
        {
            lock (ColorLock)
            {
                _targetR = r;
                _targetG = g;
                _targetB = b;
            }
        }

        private static void EnsureWorkerIsRunning()
        {
            lock (ColorLock)
            {
                if (_isWorkerRunning) return;
                _isWorkerRunning = true;
            }

            Task.Run(async () =>
            {
                try
                {
                    bool keepRunning = true;
                    byte lastSentR = 255, lastSentG = 255, lastSentB = 255;

                    while (keepRunning)
                    {
                        byte r, g, b;
                        lock (ColorLock)
                        {
                            r = _targetR;
                            g = _targetG;
                            b = _targetB;
                        }

                        // Only write if values actually changed to completely eliminate redundant USB usage
                        if (r != lastSentR || g != lastSentG || b != lastSentB)
                        {
                            // Await non-blocking asynchronous Windows I/O operation
                            await Stars80ModSystem.SetEntireKeyboardColorAsync(r, g, b).ConfigureAwait(false);
                            lastSentR = r;
                            lastSentG = g;
                            lastSentB = b;
                        }

                        if (r == 0 && g == 0 && b == 0)
                        {
                            lock (ColorLock)
                            {
                                if (_targetR == 0 && _targetG == 0 && _targetB == 0)
                                {
                                    _isWorkerRunning = false;
                                    keepRunning = false;
                                }
                            }
                        }

                        // Increased delay to 40ms (~25 FPS update rate) 
                        // This matches the physiological fusion frequency for custom keyboard fade effects perfectly!
                        await Task.Delay(40).ConfigureAwait(false);
                    }
                }
                catch
                {
                    lock (ColorLock) { _isWorkerRunning = false; }
                }
            });
        }
    }
}