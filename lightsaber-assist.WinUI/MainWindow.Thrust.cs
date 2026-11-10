using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using lightsaber_assist.Core.Analysis;
using System;
using System.Linq;
using Windows.UI;

namespace lightsaber_assist.WinUI
{
    public sealed partial class MainWindow
    {
        private static readonly Color SimpleColor = Colors.Orange;
        private static readonly Color EngagementColor = Colors.MediumOrchid;
        private bool markersDirty = true;

        private void ThrustThreshold_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
            => markersDirty = true;

        private void RefreshThrustMarkers()
        {
            markersDirty = false;
            TimelineMarkers.Children.Clear();
            var maxGap = Math.Max(analysisStep * 3, 3);
            var events = analyses.Count == 0
                ? new System.Collections.Generic.List<ThrustEvent>()
                : ThrustEventBuilder.Build(analyses.Values, ThrustThreshold.Value / 100.0, maxGap).ToList();
            RebuildEntries(events);
            var width = TimelineMarkers.ActualWidth;
            var duration = SeekSlider.Maximum;
            if (width <= 0 || duration <= 1 || events.Count == 0)
            {
                return;
            }

            foreach (var ev in events)
            {
                var x = ev.Start.TotalMilliseconds / duration * width;
                var w = Math.Max(4, (ev.End - ev.Start).TotalMilliseconds / duration * width);
                var marker = new Rectangle
                {
                    Width = w,
                    Height = 10,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = new SolidColorBrush(ev.Type == AnnotationType.ArmeEngagement ? EngagementColor : SimpleColor),
                };
                var label = ev.Type == AnnotationType.ArmeEngagement ? "Armé d'engagement" : "Armé simple";
                ToolTipService.SetToolTip(marker, $"{label} (combattant {ev.FighterId + 1}) - {ev.Confidence:P0}");
                Canvas.SetLeft(marker, x);
                Canvas.SetTop(marker, 2);
                TimelineMarkers.Children.Add(marker);
            }
        }

        private static string Label(AnnotationType type) => type switch
        {
            AnnotationType.ArmeEngagement => "Armé d'engagement",
            AnnotationType.ArmeSimple => "Armé simple",
            AnnotationType.Touche => "Touche",
            _ => type.ToString(),
        };

        private static Color ColorOf(AnnotationType type) => type switch
        {
            AnnotationType.ArmeEngagement => EngagementColor,
            AnnotationType.Touche => Colors.IndianRed,
            _ => SimpleColor,
        };

        private void RebuildEntries(System.Collections.Generic.IEnumerable<ThrustEvent> events)
        {
            var list = new System.Collections.Generic.List<TimelineEntry>();
            foreach (var a in annotations)
            {
                list.Add(new TimelineEntry(a.Timestamp, Label(a.Type),
                    string.IsNullOrWhiteSpace(a.Comment) ? $"Annotation - image {a.FrameIndex}" : $"Annotation - image {a.FrameIndex} - {a.Comment}",
                    new SolidColorBrush(ColorOf(a.Type)), a));
            }

            foreach (var ev in events)
            {
                var span = (ev.End - ev.Start).TotalSeconds;
                list.Add(new TimelineEntry(ev.Start, Label(ev.Type),
                    $"Détecté - combattant {ev.FighterId + 1} - {ev.Confidence:P0} - durée {span:0.0} s (images {ev.StartFrame}-{ev.EndFrame})",
                    new SolidColorBrush(ColorOf(ev.Type)), null));
            }

            var sorted = list.OrderBy(i => i.Timestamp).ToList();
            var common = 0;
            while (common < entries.Count && common < sorted.Count
                && entries[common].Timestamp == sorted[common].Timestamp
                && entries[common].Title == sorted[common].Title
                && entries[common].Detail == sorted[common].Detail
                && ReferenceEquals(entries[common].Annotation, sorted[common].Annotation))
            {
                common++;
            }

            while (entries.Count > common)
            {
                entries.RemoveAt(entries.Count - 1);
            }

            for (var i = common; i < sorted.Count; i++)
            {
                entries.Add(sorted[i]);
            }
        }

        private static void ComputeThrust(System.Collections.Generic.IEnumerable<FrameAnalysis> frames)
        {
            foreach (var f in frames)
            {
                ThrustDetector.Apply(f);
            }
        }
    }
}
