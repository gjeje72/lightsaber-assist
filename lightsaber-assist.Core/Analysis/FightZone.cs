namespace lightsaber_assist.Core.Analysis;

/// <summary>
/// Zone de combat (ellipse alignée sur les axes, pour tenir compte de la perspective)
/// et positions initiales des deux combattants à suivre.
/// </summary>
public sealed record FightZone(PointF Center, float RadiusX, float RadiusY, IReadOnlyList<PointF> FighterSeeds)
{
    /// <summary>
    /// Construit la zone à partir de 3 points du cercle au sol : extrémité gauche, extrémité droite et extrémité du fond.
    /// </summary>
    public static FightZone FromClicks(PointF left, PointF right, PointF back, IReadOnlyList<PointF> seeds)
    {
        var center = new PointF((left.X + right.X) / 2f, (left.Y + right.Y) / 2f);
        var rx = Math.Max(1f, Math.Abs(right.X - left.X) / 2f);
        var ry = Math.Max(1f, Math.Abs(center.Y - back.Y));
        return new FightZone(center, rx, ry, seeds);
    }

    /// <summary>Test d'appartenance d'un point au sol, avec une marge multiplicative sur les rayons.</summary>
    public bool Contains(PointF point, float margin = 1.1f)
    {
        var dx = (point.X - Center.X) / (RadiusX * margin);
        var dy = (point.Y - Center.Y) / (RadiusY * margin);
        return (dx * dx) + (dy * dy) <= 1f;
    }

    /// <summary>Point au sol d'une détection : milieu du bas de la boîte.</summary>
    public bool ContainsFeet(BoundingBox box, float margin = 1.1f) => Contains(new PointF(box.X + (box.Width / 2f), box.Bottom), margin);

    /// <summary>Marge large pour le suivi : les combattants évoluent souvent sur ou au-delà de la ligne du ring.</summary>
    public const float TrackingMargin = 1.4f;

    /// <summary>Signature courte de la zone, utilisée pour distinguer les analyses en cache.</summary>
    public string Signature =>
        FormattableString.Invariant($"z{Center.X:0}.{Center.Y:0}.{RadiusX:0}.{RadiusY:0}")
        + string.Concat(FighterSeeds.Select(s => FormattableString.Invariant($".s{s.X:0}.{s.Y:0}")));

    /// <summary>
    /// Indique si un segment longe le cercle lumineux (la majorit\u00e9 de ses points sont sur l'ellipse) :
    /// il s'agit alors du trac\u00e9 au sol et non d'un sabre.
    /// </summary>
    public bool IsAlongRing(SaberSegment segment, float tolerance = 0.12f, float minRatio = 0.8f, int samples = 9)
    {
        var onRing = 0;
        for (var i = 0; i < samples; i++)
        {
            var t = i / (float)(samples - 1);
            var x = segment.Hilt.X + ((segment.Tip.X - segment.Hilt.X) * t);
            var y = segment.Hilt.Y + ((segment.Tip.Y - segment.Hilt.Y) * t);
            var dx = (x - Center.X) / RadiusX;
            var dy = (y - Center.Y) / RadiusY;
            var r = MathF.Sqrt((dx * dx) + (dy * dy));
            if (Math.Abs(r - 1f) <= tolerance)
            {
                onRing++;
            }
        }

        return onRing >= minRatio * samples;
    }
}
