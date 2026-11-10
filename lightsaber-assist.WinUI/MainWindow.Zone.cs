using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using lightsaber_assist.Core.Analysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace lightsaber_assist.WinUI
{
    public sealed partial class MainWindow
    {
        private const int ZoneClickCount = 3;
        private const int FighterClickCount = 2;

        private static readonly string[] CalibrationPrompts =
        [
            "Zone de combat (1/5) : cliquez l'extr\u00e9mit\u00e9 GAUCHE du cercle.",
            "Zone de combat (2/5) : cliquez l'extr\u00e9mit\u00e9 DROITE du cercle.",
            "Zone de combat (3/5) : cliquez l'extr\u00e9mit\u00e9 du FOND du cercle (c\u00f4t\u00e9 oppos\u00e9 \u00e0 la cam\u00e9ra).",
            "Zone de combat (4/5) : cliquez sur le premier combattant.",
            "Zone de combat (5/5) : cliquez sur le second combattant (pas l'arbitre).",
        ];

        private FightZone? fightZone;
        private List<PointF>? calibrationPoints;

        private void InitZone()
        {
            VideoSurface.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(VideoSurface_PointerPressed), true);
        }

        private void DefineZone_Click(object sender, RoutedEventArgs e)
        {
            if (videoInfo is null)
            {
                StatusText.Text = "Ouvrez d'abord une vid\u00e9o.";
                return;
            }

            calibrationPoints = [];
            StatusText.Text = CalibrationPrompts[0];
            overlayDirty = true;
        }

        private void ClearZone_Click(object sender, RoutedEventArgs e)
        {
            calibrationPoints = null;
            if (fightZone is not null)
            {
                fightZone = null;
                ResetAnalysesAfterZoneChange();
            }

            StatusText.Text = "Zone de combat effac\u00e9e.";
            overlayDirty = true;
        }

        private void ResetAnalysesAfterZoneChange()
        {
            analyses.Clear();
            realtimeAnalyzer = null;
            markersDirty = true;
            overlayDirty = true;
        }

        private void VideoSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (calibrationPoints is null || videoInfo is null)
            {
                return;
            }

            var pos = e.GetCurrentPoint(Overlay).Position;
            double cw = Overlay.ActualWidth, ch = Overlay.ActualHeight;
            if (cw <= 0 || ch <= 0)
            {
                return;
            }

            var scale = Math.Min(cw / videoInfo.Width, ch / videoInfo.Height);
            var offX = (cw - (videoInfo.Width * scale)) / 2;
            var offY = (ch - (videoInfo.Height * scale)) / 2;
            var x = (float)((pos.X - offX) / scale);
            var y = (float)((pos.Y - offY) / scale);
            if (x < 0 || y < 0 || x > videoInfo.Width || y > videoInfo.Height)
            {
                return;
            }

            e.Handled = true;
            calibrationPoints.Add(new PointF(x, y));
            overlayDirty = true;

            if (calibrationPoints.Count < ZoneClickCount + FighterClickCount)
            {
                StatusText.Text = CalibrationPrompts[calibrationPoints.Count];
                return;
            }

            var pts = calibrationPoints;
            fightZone = FightZone.FromClicks(pts[0], pts[1], pts[2], [pts[3], pts[4]]);
            calibrationPoints = null;
            ResetAnalysesAfterZoneChange();
            StatusText.Text = "Zone de combat d\u00e9finie. Lancez (ou relancez) l'analyse.";
        }

        private void DrawZoneOverlay()
        {
            if (videoInfo is null)
            {
                return;
            }

            double cw = Overlay.ActualWidth, ch = Overlay.ActualHeight;
            if (cw <= 0 || ch <= 0)
            {
                return;
            }

            var scale = Math.Min(cw / videoInfo.Width, ch / videoInfo.Height);
            var offX = (cw - (videoInfo.Width * scale)) / 2;
            var offY = (ch - (videoInfo.Height * scale)) / 2;

            FightZone? zone = fightZone;
            IReadOnlyList<PointF> dots = zone?.FighterSeeds ?? [];
            if (calibrationPoints is { Count: > 0 } pts)
            {
                dots = pts;
                if (pts.Count >= ZoneClickCount)
                {
                    zone = FightZone.FromClicks(pts[0], pts[1], pts[2], []);
                }
            }

            if (zone is not null)
            {
                var ellipse = new Ellipse
                {
                    Width = zone.RadiusX * 2 * scale,
                    Height = zone.RadiusY * 2 * scale,
                    Stroke = new SolidColorBrush(Colors.Gold),
                    StrokeThickness = 2,
                    StrokeDashArray = [4, 3],
                };
                Canvas.SetLeft(ellipse, offX + ((zone.Center.X - zone.RadiusX) * scale));
                Canvas.SetTop(ellipse, offY + ((zone.Center.Y - zone.RadiusY) * scale));
                Overlay.Children.Add(ellipse);
            }

            foreach (var p in dots)
            {
                var dot = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Colors.Gold) };
                Canvas.SetLeft(dot, offX + (p.X * scale) - 5);
                Canvas.SetTop(dot, offY + (p.Y * scale) - 5);
                Overlay.Children.Add(dot);
            }
        }
    }
}
