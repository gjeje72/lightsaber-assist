using lightsaber_assist.Core.Analysis;
using Microsoft.UI.Xaml.Media;
using System;

namespace lightsaber_assist.WinUI
{
    public sealed class TimelineEntry(TimeSpan timestamp, string title, string detail, Brush accent, Annotation? annotation)
    {
        public TimeSpan Timestamp { get; } = timestamp;

        public string TimeText { get; } = timestamp.ToString(timestamp.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");

        public string Title { get; } = title;

        public string Detail { get; } = detail;

        public Brush Accent { get; } = accent;

        public Annotation? Annotation { get; } = annotation;
    }
}
