using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace lightsaber_assist.WinUI
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            InitAnalysis();
            var mediaPlayer = new Windows.Media.Playback.MediaPlayer();
            Player.SetMediaPlayer(mediaPlayer);
            mediaPlayer.PlaybackSession.PlaybackStateChanged += (s, _) =>
                DispatcherQueue.TryEnqueue(UpdatePlayPauseButton);
            mediaPlayer.PlaybackSession.NaturalDurationChanged += (s, _) =>
                DispatcherQueue.TryEnqueue(() =>
                {
                    var d = s.NaturalDuration;
                    SeekSlider.Maximum = Math.Max(d.TotalMilliseconds, 1);
                    DurationText.Text = Format(d);
                });
            mediaPlayer.PlaybackSession.PositionChanged += (s, _) =>
                DispatcherQueue.TryEnqueue(() =>
                {
                    var p = s.Position;
                    updatingSlider = true;
                    SeekSlider.Value = p.TotalMilliseconds;
                    updatingSlider = false;
                    PositionText.Text = Format(p);
                });
        }

        private bool updatingSlider;

        private static string Format(TimeSpan t) =>
            t.ToString(t.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");

        private void SeekSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (!updatingSlider && Player.MediaPlayer is { } mp)
            {
                mp.PlaybackSession.Position = TimeSpan.FromMilliseconds(e.NewValue);
            }
        }

        private void ZoomBy(float factor) =>
            ZoomHost.ChangeView(null, null, Math.Clamp(ZoomHost.ZoomFactor * factor, ZoomHost.MinZoomFactor, ZoomHost.MaxZoomFactor));

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomBy(1.25f);

        private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomBy(0.8f);

        private void ZoomReset_Click(object sender, RoutedEventArgs e) => ZoomHost.ChangeView(0, 0, 1f);

        private static readonly double[] Speeds = { 0.25, 0.5, 1.0 };

        private void UpdatePlayPauseButton()
        {
            var state = Player.MediaPlayer?.PlaybackSession.PlaybackState;
            PlayPauseButton.Content = state == Windows.Media.Playback.MediaPlaybackState.Playing
                ? "\u23F8 Pause"
                : "\u25B6 Lecture";
        }

        private void TogglePlayPause()
        {
            var mp = Player.MediaPlayer;
            if (mp is null)
            {
                return;
            }
            if (mp.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing)
            {
                mp.Pause();
            }
            else
            {
                mp.Play();
            }
        }

        private void ChangeSpeed(int delta)
        {
            var session = Player.MediaPlayer?.PlaybackSession;
            if (session is null)
            {
                return;
            }
            int current = 0;
            for (int i = 0; i < Speeds.Length; i++)
            {
                if (Math.Abs(Speeds[i] - session.PlaybackRate) < Math.Abs(Speeds[current] - session.PlaybackRate))
                {
                    current = i;
                }
            }
            session.PlaybackRate = Speeds[Math.Clamp(current + delta, 0, Speeds.Length - 1)];
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

        private void PlayPause_Accel(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { if (IsTyping()) return; TogglePlayPause(); e.Handled = true; }

        private void PrevFrame_Accel(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { if (IsTyping()) return; PrevFrame_Click(s, null!); e.Handled = true; }

        private void NextFrame_Accel(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { if (IsTyping()) return; NextFrame_Click(s, null!); e.Handled = true; }

        private void SpeedUp_Accel(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { if (IsTyping()) return; ChangeSpeed(1); e.Handled = true; }

        private void SpeedDown_Accel(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { if (IsTyping()) return; ChangeSpeed(-1); e.Handled = true; }

        private async void Open_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            foreach (var ext in new[] { ".mp4", ".mov", ".wmv", ".avi", ".mkv" })
            {
                picker.FileTypeFilter.Add(ext);
            }
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }
            Player.Source = Windows.Media.Core.MediaSource.CreateFromStorageFile(file);
            await OnVideoOpenedAsync(file.Path);
        }

        private void NextFrame_Click(object sender, RoutedEventArgs e)
        {
            Player.MediaPlayer?.Pause();
            Player.MediaPlayer?.StepForwardOneFrame();
        }

        private void PrevFrame_Click(object sender, RoutedEventArgs e)
        {
            Player.MediaPlayer?.Pause();
            Player.MediaPlayer?.StepBackwardOneFrame();
        }

        private void Speed_Click(object sender, RoutedEventArgs e)
        {
            if (Player.MediaPlayer is not null && sender is Button { Tag: string tag })
            {
                Player.MediaPlayer.PlaybackSession.PlaybackRate = double.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
