using System;
using System.Media;
using System.Threading.Tasks;
using ATASMultiSignal.Models;

namespace ATASMultiSignal.Alerts
{
    /// <summary>
    /// Manages trading signal alerts.  Alerts fire only on direction changes to
    /// prevent repeated notifications on the same signal.
    /// </summary>
    public sealed class AlertManager
    {
        // ── Parameters ──────────────────────────────────────────────────────────
        public bool SoundEnabled { get; set; } = true;
        public bool PopupEnabled { get; set; } = true;

        // ── State ────────────────────────────────────────────────────────────────
        private SignalDirection _lastAlertedDirection = SignalDirection.None;

        /// <summary>
        /// Fires an alert when <paramref name="newDirection"/> differs from
        /// <paramref name="lastDirection"/> and is not <see cref="SignalDirection.None"/>.
        /// </summary>
        public void TryAlert(int bar, SignalDirection newDirection, SignalDirection lastDirection)
        {
            // Only alert on a new non-neutral signal
            if (newDirection == SignalDirection.None) return;
            if (newDirection == lastDirection) return;
            if (newDirection == _lastAlertedDirection) return;

            _lastAlertedDirection = newDirection;

            if (SoundEnabled)
                PlaySoundAsync(newDirection);

            if (PopupEnabled)
                ShowPopup(bar, newDirection);
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private static void PlaySoundAsync(SignalDirection direction)
        {
            // Fire-and-forget to avoid blocking the UI thread
            Task.Run(() =>
            {
                try
                {
                    if (direction == SignalDirection.Buy)
                    {
                        // Higher pitch for buy
                        Console.Beep(880, 200);
                        System.Threading.Thread.Sleep(80);
                        Console.Beep(1100, 150);
                    }
                    else
                    {
                        // Lower pitch for sell
                        Console.Beep(660, 200);
                        System.Threading.Thread.Sleep(80);
                        Console.Beep(440, 250);
                    }
                }
                catch
                {
                    // Beep may not be available on all systems; ignore silently.
                }
            });
        }

        private static void ShowPopup(int bar, SignalDirection direction)
        {
            // ATAS does not expose a native popup API from indicator code;
            // we write to the debug output so the message appears in the ATAS log.
            string emoji = direction == SignalDirection.Buy ? "▲ BUY" : "▼ SELL";
            string msg = $"[MultiSignal] {emoji} signal on bar {bar} at {DateTime.Now:HH:mm:ss}";
            System.Diagnostics.Debug.WriteLine(msg);
            Console.WriteLine(msg);
        }
    }
}
