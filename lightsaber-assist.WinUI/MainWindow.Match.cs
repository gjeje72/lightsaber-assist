using lightsaber_assist.Core.Analysis;
using lightsaber_assist.Vision;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Capture.Frames;
using Windows.Storage.Streams;

namespace lightsaber_assist.WinUI
{
    internal sealed record ReplayEntry(TimeSpan Time, byte[][] Jpegs, int[] Widths, int[] Heights, FrameAnalysis?[] Analyses);

    internal sealed record RecordJob(TimeSpan Time, LiveFrame[] Frames, FrameAnalysis?[] Analyses);

    public sealed partial class MainWindow
    {
        private const int MaxCameras = 3;
        private const int ReplayMaxWidth = 960;
        private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan RecordInterval = TimeSpan.FromMilliseconds(66);

        private readonly List<LiveCameraSession> liveSessions = [];
        private readonly WriteableBitmap?[] liveBitmaps = new WriteableBitmap?[MaxCameras];
        private readonly List<ReplayEntry> replayBuffer = [];
        private readonly object replayLock = new();
        private CancellationTokenSource? liveCts;
        private Channel<RecordJob>? recordChannel;
        private volatile bool liveAnalyze;
        private volatile bool livePaused;
        private int pendingEncodes;
        private string[] fighterNames = [];
        private string[] liveNames = [];

        private bool replayMode;
        private ReplayEntry[] replayEntries = [];
        private int replayIndex;
        private int renderedIndex = -1;
        private bool replayPlaying;
        private bool replayRendering;
        private double replaySpeed = 1;
        private TimeSpan replayPos;
        private long replayLastTick;
        private DispatcherTimer? replayTimer;

        private Grid[] LiveTiles => [LiveTile0, LiveTile1, LiveTile2];

        private Image[] LiveImages => [LiveImage0, LiveImage1, LiveImage2];

        private Canvas[] LiveOverlays => [LiveOverlay0, LiveOverlay1, LiveOverlay2];

        private TextBlock[] LiveLabels => [LiveLabel0, LiveLabel1, LiveLabel2];

        private async void NewMatch_Click(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<MediaFrameSourceGroup> groups;
            try
            {
                groups = await MediaFrameSourceGroup.FindAllAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
                return;
            }

            if (groups.Count == 0)
            {
                StatusText.Text = "Aucune caméra détectée.";
                return;
            }

            var checks = groups.Select(g => new CheckBox { Content = g.DisplayName, Tag = g }).ToList();
            var matchName = new TextBox { Header = "Nom du match", PlaceholderText = "Ex. : Quart de finale 1/3" };
            var first1 = new TextBox { Header = "Combattant rouge (à gauche de l'arbitre) - Prénom", PlaceholderText = "Prénom" };
            var last1 = new TextBox { Header = "Nom", PlaceholderText = "Nom" };
            var country1 = new TextBox { Header = "Nationalité (code, ex. FRA)", PlaceholderText = "FRA", MaxLength = 3 };
            var first2 = new TextBox { Header = "Combattant vert (à droite de l'arbitre) - Prénom", PlaceholderText = "Prénom" };
            var last2 = new TextBox { Header = "Nom", PlaceholderText = "Nom" };
            var country2 = new TextBox { Header = "Nationalité (code, ex. FRA)", PlaceholderText = "FRA", MaxLength = 3 };
            var hint = new TextBlock { Text = "Sélectionnez de 1 à 3 caméras.", TextWrapping = TextWrapping.Wrap };
            var panel = new StackPanel { Spacing = 8, MinWidth = 360 };
            panel.Children.Add(new TextBlock { Text = "Caméras", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(hint);
            foreach (var c in checks)
            {
                panel.Children.Add(c);
            }

            panel.Children.Add(new TextBlock { Text = "Combattants", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            panel.Children.Add(matchName);
            panel.Children.Add(first1);
            panel.Children.Add(last1);
            panel.Children.Add(country1);
            panel.Children.Add(first2);
            panel.Children.Add(last2);
            panel.Children.Add(country2);

            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Nouveau match",
                Content = new ScrollViewer { Content = panel },
                PrimaryButtonText = "Commencer le match",
                CloseButtonText = "Annuler",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false,
            };

            void Update(object? s, RoutedEventArgs a)
            {
                var n = checks.Count(c => c.IsChecked == true);
                dialog.IsPrimaryButtonEnabled = n is >= 1 and <= MaxCameras;
                hint.Text = n > MaxCameras ? $"Maximum {MaxCameras} caméras." : "Sélectionnez de 1 à 3 caméras.";
            }

            foreach (var c in checks)
            {
                c.Checked += Update;
                c.Unchecked += Update;
            }

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var selected = checks.Where(c => c.IsChecked == true).Select(c => (MediaFrameSourceGroup)c.Tag).ToList();
            await StartMatchAsync(
                selected,
                [
                    new FighterInfo(first1.Text.Trim(), last1.Text.Trim(), country1.Text.Trim()),
                    new FighterInfo(first2.Text.Trim(), last2.Text.Trim(), country2.Text.Trim()),
                ],
                matchName.Text.Trim());
        }

        private void ShowMatchInfo(FighterInfo[] fighters, string matchTitle)
        {
            Fighter1Name.Text = fighters[0].DisplayName;
            Fighter2Name.Text = fighters[1].DisplayName;
            Fighter1Country.Text = fighters[0].CountryCode;
            Fighter2Country.Text = fighters[1].CountryCode;
            Fighter1Flag.Source = fighters[0].FlagUri is { } u1 ? new BitmapImage(u1) : null;
            Fighter2Flag.Source = fighters[1].FlagUri is { } u2 ? new BitmapImage(u2) : null;
            MatchTitleText.Text = matchTitle;
            MatchInfoBar.Visibility = Visibility.Visible;
        }

        private async Task StartMatchAsync(IReadOnlyList<MediaFrameSourceGroup> groups, FighterInfo[] fighters, string matchTitle)
        {
            StopLive();
            fighterNames = fighters.Select(f => f.DisplayName).ToArray();
            ShowMatchInfo(fighters, matchTitle);
            analysisCts?.Cancel();
            Player.MediaPlayer?.Pause();
            frameSource?.Dispose();
            frameSource = null;
            videoInfo = null;
            videoPath = null;
            analyses.Clear();
            annotations.Clear();
            entries.Clear();
            Overlay.Children.Clear();
            TimelineMarkers.Children.Clear();
            SeekSlider.IsEnabled = false;
            lock (replayLock)
            {
                replayBuffer.Clear();
            }

            try
            {
                foreach (var group in groups)
                {
                    liveSessions.Add(await LiveCameraSession.StartAsync(group));
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
                StopLive();
                return;
            }

            liveNames = liveSessions.Select(s => s.Name).ToArray();
            ZoomHost.Visibility = Visibility.Collapsed;
            PlayerBar.Visibility = Visibility.Collapsed;
            LiveBar.Visibility = Visibility.Visible;
            LivePanel.Visibility = Visibility.Visible;
            var tiles = LiveTiles;
            for (var i = 0; i < MaxCameras; i++)
            {
                var used = i < liveSessions.Count;
                tiles[i].Visibility = used ? Visibility.Visible : Visibility.Collapsed;
                LivePanel.ColumnDefinitions[i].Width = new GridLength(used ? 1 : 0, GridUnitType.Star);
            }

            var f1 = string.IsNullOrWhiteSpace(fighterNames[0]) ? "Combattant 1" : fighterNames[0];
            var f2 = string.IsNullOrWhiteSpace(fighterNames[1]) ? "Combattant 2" : fighterNames[1];
            MatchText.Text = $"Rouge : {f1}   |   Vert : {f2}   |   {liveSessions.Count} caméra(s)";
            ModeButton.Content = "Match en direct";

            try
            {
                EnsureDetector();
                liveAnalyze = true;
                RealtimeToggle.IsOn = true;
                StatusText.Text = "Match en cours : direct synchronisé avec analyse.";
            }
            catch (Exception ex)
            {
                liveAnalyze = false;
                StatusText.Text = ex.Message;
            }

            livePaused = false;
            liveCts = new CancellationTokenSource();
            var token = liveCts.Token;
            recordChannel = Channel.CreateBounded<RecordJob>(6);
            var reader = recordChannel.Reader;
            var sessions = liveSessions.ToArray();
            _ = Task.Run(() => EncodeLoopAsync(reader, token));
            _ = Task.Run(() => LiveLoopAsync(sessions, token));
        }

        private void StopLive()
        {
            liveCts?.Cancel();
            liveCts = null;
            recordChannel?.Writer.TryComplete();
            recordChannel = null;
            replayTimer?.Stop();
            replayMode = false;
            replayPlaying = false;
            replayEntries = [];
            livePaused = false;
            foreach (var s in liveSessions)
            {
                s.Dispose();
            }

            liveSessions.Clear();
            fighterNames = [];
            MatchInfoBar.Visibility = Visibility.Collapsed;
            lock (replayLock)
            {
                replayBuffer.Clear();
            }

            Array.Clear(liveBitmaps);
            if (LivePanel.Visibility == Visibility.Visible)
            {
                foreach (var c in LiveOverlays)
                {
                    c.Children.Clear();
                }

                foreach (var img in LiveImages)
                {
                    img.Source = null;
                }
            }

            LivePanel.Visibility = Visibility.Collapsed;
            LiveBar.Visibility = Visibility.Collapsed;
            BackToLiveButton.Visibility = Visibility.Collapsed;
            ZoomHost.Visibility = Visibility.Visible;
            PlayerBar.Visibility = Visibility.Visible;
        }

        private static readonly TimeSpan EventHistoryWindow = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan EventRefreshInterval = TimeSpan.FromMilliseconds(500);

        private async Task LiveLoopAsync(LiveCameraSession[] sessions, CancellationToken token)
        {
            var counter = 0;
            TimeSpan? lastRef = null;
            TimeSpan? lastRecorded = null;
            TimeSpan? lastEventRefresh = null;
            var history = new System.Collections.Generic.List<FrameAnalysis>();
            var waiting = Stopwatch.StartNew();
            var warned = false;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (livePaused)
                    {
                        await Task.Delay(30, token);
                        continue;
                    }

                    var latest = sessions.Select(s => s.LatestTimestamp).ToArray();
                    var ready = Enumerable.Range(0, sessions.Length).Where(i => latest[i] is not null).ToArray();
                    if (ready.Length == 0)
                    {
                        if (!warned && waiting.Elapsed > TimeSpan.FromSeconds(3))
                        {
                            warned = true;
                            DispatcherQueue.TryEnqueue(() => StatusText.Text = "Aucune image reçue des caméras après 3 s.");
                        }

                        await Task.Delay(30, token);
                        continue;
                    }

                    waiting.Reset();
                    warned = true;
                    var reference = ready.Min(i => latest[i]!.Value);
                    if (lastRef == reference)
                    {
                        await Task.Delay(10, token);
                        continue;
                    }

                    var picked = ready.Select(i => sessions[i].PickClosest(reference)).ToArray();
                    if (picked.Any(f => f is null))
                    {
                        continue;
                    }

                    var frames = picked.Select(f => f!).ToArray();
                    var allReady = ready.Length == sessions.Length;
                    lastRef = reference;
                    var results = new FrameAnalysis?[ready.Length];
                    if (liveAnalyze)
                    {
                        try
                        {
                            var det = EnsureDetector();
                            for (var i = 0; i < ready.Length; i++)
                            {
                                var session = sessions[ready[i]];
                                session.Analyzer ??= new FrameAnalyzer(det, saberDetector);
                                results[i] = session.Analyzer!.Analyze(ToRaw(frames[i], counter, reference));
                            }
                        }
                        catch (Exception ex)
                        {
                            liveAnalyze = false;
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                StatusText.Text = ex.Message;
                                RealtimeToggle.IsOn = false;
                            });
                        }
                    }

                    counter++;
                    if (livePaused)
                    {
                        continue;
                    }

                    var primary = results.FirstOrDefault(r => r is { Fighters.Count: >= 2 }) ?? results.FirstOrDefault(r => r is not null);
                    if (primary is not null)
                    {
                        ThrustDetector.Apply(primary);
                        history.Add(primary);
                        history.RemoveAll(a => reference - a.Timestamp > EventHistoryWindow);
                        if (lastEventRefresh is null || reference - lastEventRefresh.Value >= EventRefreshInterval)
                        {
                            lastEventRefresh = reference;
                            var snapshot = history.ToArray();
                            DispatcherQueue.TryEnqueue(() =>
                            {
                                if (liveSessions.Count == 0)
                                {
                                    return;
                                }

                                var maxGap = 3;
                                var events = ThrustEventBuilder.Build(snapshot, ThrustThreshold.Value / 100.0, maxGap);
                                RebuildEntries(events);
                            });
                        }
                    }

                    if (allReady && (lastRecorded is null || reference - lastRecorded.Value >= RecordInterval) && recordChannel is { } channel
                        && channel.Writer.TryWrite(new RecordJob(reference, frames, results)))
                    {
                        lastRecorded = reference;
                        Interlocked.Increment(ref pendingEncodes);
                    }

                    var done = new TaskCompletionSource();
                    if (!DispatcherQueue.TryEnqueue(() =>
                        {
                            try
                            {
                                if (!replayMode)
                                {
                                    RenderLive(frames, results, ready);
                                }
                            }
                            finally
                            {
                                done.TrySetResult();
                            }
                        }))
                    {
                        return;
                    }

                    await done.Task;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() => StatusText.Text = "Boucle du direct interrompue : " + ex);
            }
        }

        private static RawFrame ToRaw(LiveFrame f, int index, TimeSpan timestamp)
        {
            var pixels = f.Width * f.Height;
            var bgr = new byte[pixels * 3];
            for (int i = 0, s = 0, d = 0; i < pixels; i++, s += 4, d += 3)
            {
                bgr[d] = f.Bgra[s];
                bgr[d + 1] = f.Bgra[s + 1];
                bgr[d + 2] = f.Bgra[s + 2];
            }

            return new RawFrame(index, timestamp, f.Width, f.Height, bgr);
        }

        private void RenderLive(LiveFrame[] frames, FrameAnalysis?[] results, int[] tileIndexes)
        {
            if (liveSessions.Count == 0)
            {
                return;
            }

            var images = LiveImages;
            var overlays = LiveOverlays;
            var labels = LiveLabels;
            for (var k = 0; k < frames.Length; k++)
            {
                var i = tileIndexes[k];
                if (i >= MaxCameras)
                {
                    continue;
                }

                var f = frames[k];
                var bmp = liveBitmaps[i];
                if (bmp is null || bmp.PixelWidth != f.Width || bmp.PixelHeight != f.Height)
                {
                    bmp = new WriteableBitmap(f.Width, f.Height);
                    liveBitmaps[i] = bmp;
                    images[i].Source = bmp;
                }

                using (var stream = bmp.PixelBuffer.AsStream())
                {
                    stream.Write(f.Bgra, 0, f.Bgra.Length);
                }

                bmp.Invalidate();
                DrawOverlayOn(overlays[i], results[k], f.Width, f.Height);
                var result = results[k];
                labels[i].Text = result is null ? liveNames[i] : $"{liveNames[i]} - {result.Fighters.Count} combattant(s)";
            }
        }

        private async Task EncodeLoopAsync(ChannelReader<RecordJob> reader, CancellationToken token)
        {
            try
            {
                await foreach (var job in reader.ReadAllAsync(token))
                {
                    try
                    {
                        var jpegs = new byte[job.Frames.Length][];
                        for (var i = 0; i < job.Frames.Length; i++)
                        {
                            jpegs[i] = await EncodeJpegAsync(job.Frames[i]);
                        }

                        var entry = new ReplayEntry(
                            job.Time,
                            jpegs,
                            job.Frames.Select(f => f.Width).ToArray(),
                            job.Frames.Select(f => f.Height).ToArray(),
                            job.Analyses);
                        lock (replayLock)
                        {
                            replayBuffer.Add(entry);
                            while (replayBuffer.Count > 1 && entry.Time - replayBuffer[0].Time > ReplayWindow)
                            {
                                replayBuffer.RemoveAt(0);
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        Interlocked.Decrement(ref pendingEncodes);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static async Task<byte[]> EncodeJpegAsync(LiveFrame f)
        {
            var scale = Math.Min(1.0, ReplayMaxWidth / (double)f.Width);
            var w = (uint)Math.Max(2, Math.Round(f.Width * scale));
            var h = (uint)Math.Max(2, Math.Round(f.Height * scale));
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(f.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, f.Width, f.Height, BitmapAlphaMode.Ignore);
            using var stream = new InMemoryRandomAccessStream();
            var props = new BitmapPropertySet
            {
                { "ImageQuality", new BitmapTypedValue(0.7f, Windows.Foundation.PropertyType.Single) },
            };
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, props);
            encoder.SetSoftwareBitmap(bitmap);
            encoder.BitmapTransform.ScaledWidth = w;
            encoder.BitmapTransform.ScaledHeight = h;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Linear;
            await encoder.FlushAsync();
            var bytes = new byte[stream.Size];
            stream.Seek(0);
            await stream.ReadAsync(bytes.AsBuffer(), (uint)stream.Size, InputStreamOptions.None);
            return bytes;
        }

        private async void LiveReview_Click(object sender, RoutedEventArgs e)
        {
            if (liveSessions.Count == 0 || replayMode)
            {
                return;
            }

            livePaused = true;
            for (var i = 0; i < 100 && Volatile.Read(ref pendingEncodes) > 0; i++)
            {
                await Task.Delay(30);
            }

            ReplayEntry[] snapshot;
            lock (replayLock)
            {
                snapshot = replayBuffer.ToArray();
            }

            if (snapshot.Length == 0)
            {
                livePaused = false;
                StatusText.Text = "Pas encore d'images enregistrées.";
                return;
            }

            replayEntries = snapshot;
            replayIndex = snapshot.Length - 1;
            renderedIndex = -1;
            replayPos = ReplayOffset(replayIndex);
            replayPlaying = false;
            replaySpeed = 1;
            replayMode = true;
            if (replayTimer is null)
            {
                replayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
                replayTimer.Tick += ReplayTimer_Tick;
            }

            replayTimer.Start();
            LiveBar.Visibility = Visibility.Collapsed;
            PlayerBar.Visibility = Visibility.Visible;
            BackToLiveButton.Visibility = Visibility.Visible;
            SeekSlider.IsEnabled = true;
            SeekSlider.Maximum = Math.Max(ReplayOffset(snapshot.Length - 1).TotalMilliseconds, 1);
            DurationText.Text = Format(ReplayOffset(snapshot.Length - 1));
            UpdateSpeedUi(1);
            UpdatePlayPauseButton();
            UpdateReplayPosition();
            StatusText.Text = "Direct et analyse en pause : revue des dernières minutes.";
            await ShowReplayFrameAsync();
        }

        private async void EndMatch_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Terminer le match",
                Content = "Arrêter le direct et l'analyse ? Le tampon de revue sera perdu.",
                PrimaryButtonText = "Terminer",
                CloseButtonText = "Annuler",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            StopLive();
            liveAnalyze = false;
            RealtimeToggle.IsOn = false;
            MatchText.Text = string.Empty;
            ModeButton.Content = "Source : aucune";
            StatusText.Text = "Match terminé.";
        }

        private void BackToLive_Click(object sender, RoutedEventArgs e)
        {
            replayTimer?.Stop();
            replayPlaying = false;
            replayMode = false;
            replayEntries = [];
            Array.Clear(liveBitmaps);
            BackToLiveButton.Visibility = Visibility.Collapsed;
            PlayerBar.Visibility = Visibility.Collapsed;
            LiveBar.Visibility = Visibility.Visible;
            SeekSlider.IsEnabled = false;
            livePaused = false;
            StatusText.Text = "Retour au direct.";
        }

        private TimeSpan ReplayOffset(int index) => replayEntries[index].Time - replayEntries[0].Time;

        private void UpdateReplayPosition()
        {
            updatingSlider = true;
            SeekSlider.Value = Math.Min(replayPos.TotalMilliseconds, SeekSlider.Maximum);
            updatingSlider = false;
            PositionText.Text = Format(replayPos);
        }

        private int IndexAt(TimeSpan offset)
        {
            var target = replayEntries[0].Time + offset;
            var index = 0;
            while (index + 1 < replayEntries.Length && replayEntries[index + 1].Time <= target)
            {
                index++;
            }

            return index;
        }

        private async void ReplayTimer_Tick(object? sender, object e)
        {
            var now = Stopwatch.GetTimestamp();
            if (!replayMode || !replayPlaying)
            {
                replayLastTick = now;
                return;
            }

            var dt = Stopwatch.GetElapsedTime(replayLastTick, now);
            replayLastTick = now;
            replayPos += dt * replaySpeed;
            var end = ReplayOffset(replayEntries.Length - 1);
            if (replayPos >= end)
            {
                replayPos = end;
                replayPlaying = false;
                UpdatePlayPauseButton();
            }

            replayIndex = IndexAt(replayPos);
            UpdateReplayPosition();
            await ShowReplayFrameAsync();
        }

        private void SeekReplay(TimeSpan offset)
        {
            replayPos = offset;
            replayIndex = IndexAt(offset);
            PositionText.Text = Format(replayPos);
            _ = ShowReplayFrameAsync();
        }

        private void StepReplay(int delta)
        {
            replayPlaying = false;
            UpdatePlayPauseButton();
            replayIndex = Math.Clamp(replayIndex + delta, 0, replayEntries.Length - 1);
            replayPos = ReplayOffset(replayIndex);
            UpdateReplayPosition();
            _ = ShowReplayFrameAsync();
        }

        private void ToggleReplayPlay()
        {
            if (!replayPlaying && replayIndex >= replayEntries.Length - 1)
            {
                replayPos = TimeSpan.Zero;
                replayIndex = 0;
            }

            replayPlaying = !replayPlaying;
            replayLastTick = Stopwatch.GetTimestamp();
            UpdatePlayPauseButton();
            UpdateReplayPosition();
            _ = ShowReplayFrameAsync();
        }

        private void ChangeReplaySpeed(int delta)
        {
            var current = 0;
            for (var i = 0; i < Speeds.Length; i++)
            {
                if (Math.Abs(Speeds[i] - replaySpeed) < Math.Abs(Speeds[current] - replaySpeed))
                {
                    current = i;
                }
            }

            SetRate(Speeds[Math.Clamp(current + delta, 0, Speeds.Length - 1)]);
        }

        private void SetRate(double rate)
        {
            if (replayMode)
            {
                replaySpeed = rate;
                UpdateSpeedUi(rate);
            }
            else if (Player.MediaPlayer is { } mp)
            {
                mp.PlaybackSession.PlaybackRate = rate;
            }
        }

        private double GetRate() => replayMode ? replaySpeed : Player.MediaPlayer?.PlaybackSession.PlaybackRate ?? 1;

        private async Task ShowReplayFrameAsync()
        {
            if (replayRendering)
            {
                return;
            }

            replayRendering = true;
            try
            {
                var images = LiveImages;
                var overlays = LiveOverlays;
                var labels = LiveLabels;
                while (replayMode && renderedIndex != replayIndex)
                {
                    renderedIndex = replayIndex;
                    var entry = replayEntries[renderedIndex];
                    for (var i = 0; i < entry.Jpegs.Length && i < MaxCameras; i++)
                    {
                        var bmp = new BitmapImage();
                        using var ms = new InMemoryRandomAccessStream();
                        await ms.WriteAsync(entry.Jpegs[i].AsBuffer());
                        ms.Seek(0);
                        await bmp.SetSourceAsync(ms);
                        images[i].Source = bmp;
                        DrawOverlayOn(overlays[i], entry.Analyses[i], entry.Widths[i], entry.Heights[i]);
                        labels[i].Text = liveNames.Length > i ? liveNames[i] : string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
            finally
            {
                replayRendering = false;
            }
        }
    }
}
