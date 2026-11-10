using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using lightsaber_assist.Core.Analysis;
using lightsaber_assist.Storage;
using lightsaber_assist.Video;
using lightsaber_assist.Vision;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace lightsaber_assist.WinUI
{
    public sealed partial class MainWindow
    {
        private const string ModelName = "yolo11n-pose";
        private const string ModelFileName = ModelName + ".onnx";
        private const string AnalysisVersion = ModelName + "+track4+saber3";

        private string CurrentAnalysisVersion => fightZone is null ? AnalysisVersion : AnalysisVersion + "+" + fightZone.Signature;
        private readonly ISaberDetector saberDetector = new BrightSegmentDetector();

        private readonly Dictionary<int, FrameAnalysis> analyses = [];
        private readonly ObservableCollection<Annotation> annotations = [];
        private readonly ObservableCollection<TimelineEntry> entries = [];
        private readonly IAnalysisCache analysisCache = new JsonAnalysisCache();
        private readonly IAnnotationStore annotationStore = new JsonAnnotationStore();
        private DispatcherTimer? overlayTimer;
        private YoloPoseDetector? detector;
        private FrameAnalyzer? realtimeAnalyzer;
        private FFmpegFrameSource? frameSource;
        private VideoInfo? videoInfo;
        private CancellationTokenSource? analysisCts;
        private string? videoPath;
        private int analysisStep = 1;
        private bool realtimeBusy;
        private bool overlayDirty = true;
        private int lastDrawnIndex = int.MinValue;

        private void InitAnalysis()
        {
            AnnotationTypeBox.ItemsSource = Enum.GetValues<AnnotationType>();
            AnnotationTypeBox.SelectedIndex = 0;
            AnnotationList.ItemsSource = entries;
            annotations.CollectionChanged += (_, _) => markersDirty = true;
            Overlay.SizeChanged += (_, _) => overlayDirty = true;
            TimelineMarkers.SizeChanged += (_, _) => markersDirty = true;
            overlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            overlayTimer.Tick += OverlayTimer_Tick;
            overlayTimer.Start();
            InitZone();
        }

        private async Task OnVideoOpenedAsync(string path)
        {
            analysisCts?.Cancel();
            videoPath = path;
            analyses.Clear();
            markersDirty = true;
            annotations.Clear();
            realtimeAnalyzer = null;
            fightZone = null;
            calibrationPoints = null;
            frameSource?.Dispose();
            frameSource = null;
            videoInfo = null;
            analysisStep = 1;
            overlayDirty = true;
            StatusText.Text = string.Empty;

            try
            {
                EnsureFrameSource();
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }

            try
            {
                var cached = await analysisCache.LoadAsync(path, AnalysisVersion, CancellationToken.None);
                if (cached is not null)
                {
                    ComputeThrust(cached.Frames);
                    markersDirty = true;
                    foreach (var f in cached.Frames)
                    {
                        analyses[f.FrameIndex] = f;
                    }

                    if (cached.Frames.Count >= 2)
                    {
                        analysisStep = Math.Max(1, cached.Frames[1].FrameIndex - cached.Frames[0].FrameIndex);
                    }

                    StatusText.Text = "Analyse r\u00e9cup\u00e9r\u00e9e depuis le cache.";
                }

                foreach (var a in await annotationStore.LoadAsync(path, CancellationToken.None))
                {
                    annotations.Add(a);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
        }

        private YoloPoseDetector EnsureDetector()
        {
            if (detector is not null)
            {
                return detector;
            }
            var searched = new[]
            {
                System.IO.Path.Combine(AppContext.BaseDirectory, "Models", ModelFileName),
                System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Models", ModelFileName),
            };
            var modelPath = searched.FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException("Modèle absent. Chemins testés : " + string.Join(" | ", searched));

            detector = new YoloPoseDetector(modelPath);
            return detector;
        }

        private FFmpegFrameSource EnsureFrameSource()
        {
            if (frameSource is not null)
            {
                return frameSource;
            }

            if (videoPath is null)
            {
                throw new InvalidOperationException("Aucune vid\u00e9o ouverte.");
            }

            frameSource = new FFmpegFrameSource(videoPath);
            videoInfo = frameSource.Info;
            return frameSource;
        }

        private async void Analyze_Click(object sender, RoutedEventArgs e)
        {
            if (videoPath is null)
            {
                StatusText.Text = "Ouvrez d'abord une vid\u00e9o.";
                return;
            }

            analysisCts = new CancellationTokenSource();
            var token = analysisCts.Token;
            AnalyzeButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            AnalysisProgress.Value = 0;
            try
            {
                var det = EnsureDetector();
                var source = EnsureFrameSource();
                StatusText.Text = "Analyse en cours...";
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                double smoothedRate = 0, lastValue = 0, lastSeconds = 0;
                var progress = new Progress<double>(v =>
                {
                    AnalysisProgress.Value = v;
                    var elapsed = stopwatch.Elapsed.TotalSeconds;
                    var dt = elapsed - lastSeconds;
                    if (dt >= 0.5 && v > lastValue)
                    {
                        var rate = (v - lastValue) / dt;
                        smoothedRate = smoothedRate == 0 ? rate : (0.8 * smoothedRate) + (0.2 * rate);
                        lastValue = v;
                        lastSeconds = elapsed;
                    }

                    var text = $"Analyse en cours... {v:P0}";
                    if (v >= 0.02 && v < 1 && smoothedRate > 0 && elapsed >= 3)
                    {
                        var remaining = TimeSpan.FromSeconds((1 - v) / smoothedRate);
                        text += remaining.TotalMinutes >= 1
                            ? $" – ~{(int)remaining.TotalMinutes} min {remaining.Seconds:00} s restantes"
                            : $" – ~{Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))} s restantes";
                    }

                    StatusText.Text = text;
                });
                var result = await new VideoPreAnalyzer(source, det, saberDetector, fightZone)
                    .AnalyzeAsync(videoPath, CurrentAnalysisVersion, 1, progress, token);

                analyses.Clear();
                foreach (var f in result.Frames)
                {
                    analyses[f.FrameIndex] = f;
                }

                analysisStep = 1;
                markersDirty = true;
                overlayDirty = true;
                result.Annotations = annotations.ToList();
                await analysisCache.SaveAsync(result, CancellationToken.None);                StatusText.Text = $"Analyse termin\u00e9e ({result.Frames.Count} frames).";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Analyse annul\u00e9e.";
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
            finally
            {
                AnalyzeButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
            }
        }

        private void CancelAnalysis_Click(object sender, RoutedEventArgs e) => analysisCts?.Cancel();

        private void Realtime_Toggled(object sender, RoutedEventArgs e)
        {
            liveAnalyze = RealtimeToggle.IsOn;
            if (!RealtimeToggle.IsOn)
            {
                return;
            }

            if (liveSessions.Count > 0)
            {
                try
                {
                    EnsureDetector();
                }
                catch (Exception ex)
                {
                    StatusText.Text = ex.Message;
                    RealtimeToggle.IsOn = false;
                }

                return;
            }

            try
            {
                EnsureDetector();
                EnsureFrameSource();
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
                RealtimeToggle.IsOn = false;
            }
        }

        private void OverlayOption_Click(object sender, RoutedEventArgs e) => overlayDirty = true;

        private int CurrentFrameIndex()
        {
            var fps = videoInfo?.Fps ?? 0;
            var pos = Player.MediaPlayer?.PlaybackSession.Position ?? TimeSpan.Zero;
            return fps > 0 ? (int)Math.Round(pos.TotalSeconds * fps) : 0;
        }

        private FrameAnalysis? Lookup(int index)
        {
            if (analyses.TryGetValue(index, out var a))
            {
                return a;
            }

            return analysisStep > 1 && analyses.TryGetValue(index / analysisStep * analysisStep, out a) ? a : null;
        }

        private async void OverlayTimer_Tick(object? sender, object e)
        {
            if (videoInfo is null || Player.MediaPlayer is null)
            {
                return;
            }

            var index = CurrentFrameIndex();
            if (markersDirty)
            {
                RefreshThrustMarkers();
            }
            var analysis = Lookup(index);

            if (analysis is null && RealtimeToggle.IsOn && !realtimeBusy)
            {
                realtimeBusy = true;
                try
                {
                    analysis = await Task.Run(() => AnalyzeLive(index));
                    if (analysis is not null)
                    {
                        analyses[index] = analysis;
                        markersDirty = true;
                    }
                }
                catch (Exception ex)
                {
                    StatusText.Text = ex.Message;
                    RealtimeToggle.IsOn = false;
                }
                finally
                {
                    realtimeBusy = false;
                }
            }

            if (index != lastDrawnIndex || overlayDirty)
            {
                lastDrawnIndex = index;
                overlayDirty = false;
                DrawOverlay(analysis);
            }
        }

        private FrameAnalysis? AnalyzeLive(int index)
        {
            var det = EnsureDetector();
            var frame = EnsureFrameSource().GetFrame(index);
            if (frame is null)
            {
                return null;
            }

            realtimeAnalyzer ??= new FrameAnalyzer(det, saberDetector, fightZone);
            return realtimeAnalyzer.Analyze(frame);
        }

        private void DrawOverlay(FrameAnalysis? analysis)
        {
            if (videoInfo is null)
            {
                Overlay.Children.Clear();
                return;
            }

            DrawOverlayOn(Overlay, analysis, videoInfo.Width, videoInfo.Height);
            DrawZoneOverlay();
        }

        private void DrawOverlayOn(Canvas Overlay, FrameAnalysis? analysis, int videoWidth, int videoHeight)
        {
            Overlay.Children.Clear();
            if (analysis is null)
            {
                return;
            }

            double cw = Overlay.ActualWidth, ch = Overlay.ActualHeight;
            if (cw <= 0 || ch <= 0)
            {
                return;
            }

            var scale = Math.Min(cw / videoWidth, ch / videoHeight);
            var offX = (cw - videoWidth * scale) / 2;
            var offY = (ch - videoHeight * scale) / 2;
            double X(float x) => offX + x * scale;
            double Y(float y) => offY + y * scale;

            foreach (var fighter in analysis.Fighters)
            {
                var brush = new SolidColorBrush(fighter.FighterId == 0 ? Colors.OrangeRed : Colors.LimeGreen);

                if (fighter.FighterId < fighterNames.Length && !string.IsNullOrWhiteSpace(fighterNames[fighter.FighterId]))
                {
                    var nameLabel = new TextBlock { Text = fighterNames[fighter.FighterId], Foreground = brush, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
                    Canvas.SetLeft(nameLabel, X(fighter.Box.X));
                    Canvas.SetTop(nameLabel, Y(fighter.Box.Y) - 22);
                    Overlay.Children.Add(nameLabel);
                }

                if (ShowBoxes.IsChecked == true)
                {
                    var rect = new Rectangle
                    {
                        Width = fighter.Box.Width * scale,
                        Height = fighter.Box.Height * scale,
                        Stroke = brush,
                        StrokeThickness = 2,
                    };
                    Canvas.SetLeft(rect, X(fighter.Box.X));
                    Canvas.SetTop(rect, Y(fighter.Box.Y));
                    Overlay.Children.Add(rect);
                }

                if (ShowSkeleton.IsChecked == true)
                {
                    foreach (var (from, to) in Skeleton.Bones)
                    {
                        var a = fighter.Skeleton[from];
                        var b = fighter.Skeleton[to];
                        if (a.Confidence < FighterMetrics.MinConfidence || b.Confidence < FighterMetrics.MinConfidence)
                        {
                            continue;
                        }

                        Overlay.Children.Add(new Line
                        {
                            X1 = X(a.X),
                            Y1 = Y(a.Y),
                            X2 = X(b.X),
                            Y2 = Y(b.Y),
                            Stroke = brush,
                            StrokeThickness = 2,
                        });
                    }

                    foreach (var k in fighter.Skeleton.Keypoints.Where(k => k.Confidence >= FighterMetrics.MinConfidence))
                    {
                        AddDot(Overlay, X(k.X), Y(k.Y), 3, brush);
                    }
                }

                if (ShowCog.IsChecked == true)
                {
                    AddDot(Overlay, X(fighter.CenterOfGravity.X), Y(fighter.CenterOfGravity.Y), 6, new SolidColorBrush(Colors.Yellow));
                }

                if (ShowSabers.IsChecked == true && fighter.Saber is { } saber)
                {
                    Overlay.Children.Add(new Line
                    {
                        X1 = X(saber.Hilt.X),
                        Y1 = Y(saber.Hilt.Y),
                        X2 = X(saber.Tip.X),
                        Y2 = Y(saber.Tip.Y),
                        Stroke = brush,
                        StrokeThickness = 4,
                    });
                    AddDot(Overlay, X(saber.Hilt.X), Y(saber.Hilt.Y), 5, new SolidColorBrush(Colors.White));
                }
            }

            if (ShowDistance.IsChecked == true && analysis.DistancePixels is { } px && analysis.Fighters.Count >= 2)
            {
                var a = analysis.Fighters[0].CenterOfGravity;
                var b = analysis.Fighters[1].CenterOfGravity;
                Overlay.Children.Add(new Line
                {
                    X1 = X(a.X),
                    Y1 = Y(a.Y),
                    X2 = X(b.X),
                    Y2 = Y(b.Y),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 2,
                    StrokeDashArray = [4, 2],
                });
                var norm = analysis.DistanceNormalized is { } n ? $" / {n:0.00} h" : string.Empty;
                var label = new TextBlock
                {
                    Text = $"{px:0} px{norm}",
                    Foreground = new SolidColorBrush(Colors.White),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                };
                Canvas.SetLeft(label, X((a.X + b.X) / 2));
                Canvas.SetTop(label, Y((a.Y + b.Y) / 2) - 20);
                Overlay.Children.Add(label);
            }
        }

        private static void AddDot(Canvas Overlay, double x, double y, double radius, Brush brush)
        {
            var dot = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = brush };
            Canvas.SetLeft(dot, x - radius);
            Canvas.SetTop(dot, y - radius);
            Overlay.Children.Add(dot);
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            if (analyses.Count == 0 || videoPath is null)
            {
                StatusText.Text = "Aucune analyse \u00e0 exporter.";
                return;
            }

            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(videoPath) + ".analysis",
            };
            picker.FileTypeChoices.Add("JSON", [".json"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            try
            {
                var result = new AnalysisResult
                {
                    VideoPath = videoPath,
                    Width = videoInfo?.Width ?? 0,
                    Height = videoInfo?.Height ?? 0,
                    Fps = videoInfo?.Fps ?? 0,
                    ModelVersion = CurrentAnalysisVersion,
                    Frames = analyses.Values.OrderBy(f => f.FrameIndex).ToList(),
                    Annotations = annotations.ToList(),
                };
                await new JsonAnalysisExporter().ExportAsync(result, file.Path, CancellationToken.None);
                StatusText.Text = "Export termin\u00e9.";
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
        }

        private async void AddAnnotation_Click(object sender, RoutedEventArgs e)
        {
            if (videoPath is null || Player.MediaPlayer is null || AnnotationTypeBox.SelectedItem is not AnnotationType type)
            {
                return;
            }

            var annotation = new Annotation
            {
                Timestamp = Player.MediaPlayer.PlaybackSession.Position,
                FrameIndex = CurrentFrameIndex(),
                Type = type,
                Comment = string.IsNullOrWhiteSpace(AnnotationComment.Text) ? null : AnnotationComment.Text.Trim(),
            };

            var at = 0;
            while (at < annotations.Count && annotations[at].Timestamp <= annotation.Timestamp)
            {
                at++;
            }

            annotations.Insert(at, annotation);
            AnnotationComment.Text = string.Empty;
            await SaveAnnotationsAsync();
        }

        private async void DeleteAnnotation_Click(object sender, RoutedEventArgs e)
        {
            if (AnnotationList.SelectedItem is TimelineEntry { Annotation: { } selected })
            {
                annotations.Remove(selected);
                await SaveAnnotationsAsync();
            }
        }

        private void AnnotationList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TimelineEntry a && Player.MediaPlayer is { } mp)
            {
                mp.Pause();
                mp.PlaybackSession.Position = a.Timestamp;
            }
        }

        private async Task SaveAnnotationsAsync()
        {
            if (videoPath is null)
            {
                return;
            }

            try
            {
                await annotationStore.SaveAsync(videoPath, annotations.ToList(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
        }

        private bool IsTyping() =>
            Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox;
    }
}
