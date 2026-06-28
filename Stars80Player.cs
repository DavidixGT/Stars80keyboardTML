using System;
using Terraria;
using Terraria.ModLoader;

namespace stars80qmkmod
{
    public class Stars80Player : ModPlayer
    {
        // 60 frames = exactly 1 second at standard 60FPS
        private const int MaxFlashDuration = 60; 
        private int _flashTimer = 0;
        private bool _isFadingActive = false;

        // Triggers the exact frame your character takes damage
        public override void OnHurt(Player.HurtInfo info)
        {
            if (Main.dedServ || !OperatingSystem.IsWindows()) return;

            // Start or refresh the 1-second countdown clock
            _flashTimer = MaxFlashDuration;
            _isFadingActive = true;

            // Hit frame: Instantly jump to full, solid red (255, 0, 0)
            Stars80ModSystem.SetEntireKeyboardColor(255, 0, 0);
        }

        public override void PostUpdate()
        {
            // Only execute if we are actively tracking an injury event inside a live world session
            if (Main.dedServ || Main.gameMenu || !Player.active || !_isFadingActive) 
                return;

            _flashTimer--;

            if (_flashTimer > 0)
            {
                // Calculate the fading ratio percentage (starts at 1.0 down to 0.0)
                double fadeRatio = (double)_flashTimer / MaxFlashDuration;

                // Linearly scale down the Red value based on the remaining time ratio
                byte currentRed = (byte)(fadeRatio * 255);

                // Continuously update the keyboard using your lightning-fast 17-packet batch API
                Stars80ModSystem.SetEntireKeyboardColor(currentRed, 0, 0);
            }
            else
            {
                // Force a single absolute blackout check on the final clock tick frame
                Stars80ModSystem.ClearAllKeys();
                _isFadingActive = false;
            }
        }
    }
}
