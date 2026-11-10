using lightsaber_assist.Core.Analysis;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;

namespace lightsaber_assist.WinUI
{
    public sealed partial class MainWindow
    {
        private bool updatingSpeed;

        private async Task OpenVideoFileAsync(Windows.Storage.StorageFile file)
        {
            StopLive();
            Player.Source = MediaSource.CreateFromStorageFile(file);
            ModeButton.Content = "Vidéo : " + file.Name;
            SeekSlider.IsEnabled = true;
            await OnVideoOpenedAsync(file.Path);
        }

        private async void ReopenAnalysis_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".json");
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            try
            {
                AnalysisResult result;
                await using (var stream = System.IO.File.OpenRead(file.Path))
                {
                    result = await JsonSerializer.DeserializeAsync(stream, AnalysisJsonContext.Default.AnalysisResult)
                        ?? throw new InvalidOperationException("Fichier d'analyse invalide.");
                }

                if (!System.IO.File.Exists(result.VideoPath))
                {
                    StatusText.Text = "Vidéo introuvable : " + result.VideoPath;
                    return;
                }

                var video = await Windows.Storage.StorageFile.GetFileFromPathAsync(result.VideoPath);
                await OpenVideoFileAsync(video);

                analyses.Clear();
                ComputeThrust(result.Frames);
                foreach (var f in result.Frames)
                {
                    analyses[f.FrameIndex] = f;
                }

                if (result.Frames.Count >= 2)
                {
                    analysisStep = Math.Max(1, result.Frames[1].FrameIndex - result.Frames[0].FrameIndex);
                }

                annotations.Clear();
                foreach (var a in result.Annotations.OrderBy(a => a.Timestamp))
                {
                    annotations.Add(a);
                }

                markersDirty = true;
                overlayDirty = true;
                ModeButton.Content = "Analyse : " + System.IO.Path.GetFileName(file.Path);
                StatusText.Text = "Analyse rouverte.";
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
        }

        private void UpdateSpeedUi(double rate)
        {
            updatingSpeed = true;
            SpeedSlider.Value = Math.Clamp(rate, SpeedSlider.Minimum, SpeedSlider.Maximum);
            SpeedText.Text = rate.ToString("0.00", CultureInfo.InvariantCulture) + "x";
            SpeedButton.Content = rate.ToString("0.##", CultureInfo.InvariantCulture) + "x";
            updatingSpeed = false;
        }

        private void SpeedSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (!updatingSpeed)
            {
                SetRate(e.NewValue);
            }
        }

        private void SpeedStep_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string tag })
            {
                var delta = double.Parse(tag, CultureInfo.InvariantCulture);
                SetRate(Math.Clamp(GetRate() + delta, SpeedSlider.Minimum, SpeedSlider.Maximum));
            }
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            if (Player.MediaPlayer is { } mp)
            {
                mp.IsMuted = !mp.IsMuted;
                VolumeIcon.Glyph = mp.IsMuted ? "\uE74F" : "\uE767";
            }
        }

        private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (Player.MediaPlayer is { } mp)
            {
                mp.Volume = e.NewValue / 100.0;
            }
        }

        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            var full = AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
            AppWindow.SetPresenter(full ? AppWindowPresenterKind.Default : AppWindowPresenterKind.FullScreen);
            FullscreenIcon.Glyph = full ? "\uE740" : "\uE73F";
        }
    }
}
