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
            var width = TimelineMarkers.ActualWidth;
            var duration = SeekSlider.Maximum;
            if (width <= 0 || duration <= 1 || analyses.Count == 0)
            {
                return;
            }

            var maxGap = Math.Max(analysisStep * 3, 3);
            var events = ThrustEventBuilder.Build(analyses.Values, ThrustThreshold.Value / 100.0, maxGap);
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

        private static void ComputeThrust(System.Collections.Generic.IEnumerable<FrameAnalysis> frames)
        {
            foreach (var f in frames)
            {
                ThrustDetector.Apply(f);
            }
        }
    }
}
