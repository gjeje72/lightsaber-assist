using lightsaber_assist.Core.Analysis;

namespace lightsaber_assist.Vision;

/// <summary>
/// Suivi de 2 combattants par continuité du mouvement (aucun critère de couleur) :
/// une détection n'est associée à un identifiant que si elle reste proche de la position prédite.
/// Un arbitre ou un spectateur ne peut donc pas « voler » l'identité d'un combattant temporairement masqué.
/// </summary>
public sealed class FighterTracker
{
    public const int MaxTracks = 2;

    /// <summary>Saut maximal toléré entre deux frames, en hauteurs de boîte.</summary>
    public const float BaseGate = 0.6f;

    /// <summary>Tolérance supplémentaire par frame manquée, en hauteurs de boîte.</summary>
    public const float GateGrowthPerMissedFrame = 0.1f;

    public const float MaxGate = 1.5f;

    /// <summary>Après ce nombre de frames sans détection, la piste peut être reprise n'importe où dans la zone autorisée.</summary>
    public const int ReacquireAfterFrames = 45;

    /// <summary>Les graines cliquées ne sont pas exactement au centre de la boîte : tolérance initiale élargie.</summary>
    private const int SeedInitialMissed = 5;

    private const float VelocitySmoothing = 0.5f;
    private const int MaxPredictionFrames = 5;

    private sealed class Track
    {
        public PointF Center;
        public PointF Velocity;
        public float Height;
        public int Missed;
    }

    private readonly Dictionary<int, Track> tracks = [];
    private bool seeded;

    public void Reset()
    {
        tracks.Clear();
        seeded = false;
    }

    /// <summary>Initialise les combattants à suivre (ids par X croissant) et interdit la création d'autres identifiants.</summary>
    public void Seed(IReadOnlyList<PointF> seeds)
    {
        tracks.Clear();
        var ordered = seeds.OrderBy(s => s.X).Take(MaxTracks).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            tracks[i] = new Track { Center = ordered[i], Missed = SeedInitialMissed };
        }

        seeded = ordered.Count > 0;
    }

    /// <param name="detections">Toutes les détections de la frame.</param>
    /// <param name="canStartTrack">
    /// Filtre appliqué uniquement pour créer ou reprendre une piste (par exemple : pieds dans la zone de combat).
    /// Les détections qui prolongent une piste existante n'y sont pas soumises.
    /// </param>
    public List<(int Id, BoundingBox Box, Skeleton Skeleton)> Assign(
        IReadOnlyList<(BoundingBox Box, Skeleton Skeleton)> detections,
        Func<BoundingBox, bool>? canStartTrack = null)
    {
        var result = new List<(int, BoundingBox, Skeleton)>();
        var usedDets = new HashSet<int>();
        var matched = new HashSet<int>();

        var pairs = new List<(int Id, int Det, double Dist)>();
        foreach (var (id, track) in tracks)
        {
            var predicted = Predict(track);
            for (var d = 0; d < detections.Count; d++)
            {
                var box = detections[d].Box;
                var scale = track.Height > 0 ? track.Height : box.Height;
                if (scale <= 0 || !SimilarSize(track, box))
                {
                    continue;
                }

                var dist = FighterMetrics.DistancePixels(predicted, box.Center) / scale;
                if (dist <= Gate(track))
                {
                    pairs.Add((id, d, dist));
                }
            }
        }

        foreach (var (id, d, _) in pairs.OrderBy(p => p.Dist))
        {
            if (matched.Contains(id) || usedDets.Contains(d))
            {
                continue;
            }

            matched.Add(id);
            usedDets.Add(d);
            Update(tracks[id], detections[d].Box);
            result.Add((id, detections[d].Box, detections[d].Skeleton));
        }

        // Pistes perdues depuis longtemps : reprise par la détection libre la plus proche (dans la zone autorisée).
        foreach (var (id, track) in tracks.Where(t => !matched.Contains(t.Key) && t.Value.Missed >= ReacquireAfterFrames).ToList())
        {
            var best = -1;
            var bestDist = double.MaxValue;
            for (var d = 0; d < detections.Count; d++)
            {
                if (usedDets.Contains(d) || !SimilarSize(track, detections[d].Box) || (canStartTrack is not null && !canStartTrack(detections[d].Box)))
                {
                    continue;
                }

                var dist = FighterMetrics.DistancePixels(track.Center, detections[d].Box.Center);
                if (dist < bestDist)
                {
                    best = d;
                    bestDist = dist;
                }
            }

            if (best >= 0)
            {
                track.Velocity = new PointF(0, 0);
                matched.Add(id);
                usedDets.Add(best);
                Update(track, detections[best].Box);
                result.Add((id, detections[best].Box, detections[best].Skeleton));
            }
        }

        foreach (var (id, track) in tracks)
        {
            if (!matched.Contains(id))
            {
                track.Missed++;
            }
        }

        if (!seeded)
        {
            StartNewTracks(detections, usedDets, canStartTrack, result);
        }

        return result.OrderBy(r => r.Item1).ToList();
    }

    private void StartNewTracks(
        IReadOnlyList<(BoundingBox Box, Skeleton Skeleton)> detections,
        HashSet<int> usedDets,
        Func<BoundingBox, bool>? canStartTrack,
        List<(int, BoundingBox, Skeleton)> result)
    {
        var free = Enumerable.Range(0, detections.Count)
            .Where(d => !usedDets.Contains(d) && (canStartTrack is null || canStartTrack(detections[d].Box)))
            .OrderByDescending(d => detections[d].Box.Confidence)
            .ToList();

        foreach (var d in free)
        {
            if (tracks.Count >= MaxTracks)
            {
                break;
            }

            var id = tracks.Count == 0 ? 0 : Enumerable.Range(0, MaxTracks).First(i => !tracks.ContainsKey(i));
            var track = new Track { Center = detections[d].Box.Center, Height = detections[d].Box.Height };
            tracks[id] = track;
            usedDets.Add(d);
            result.Add((id, detections[d].Box, detections[d].Skeleton));
        }
    }

    private static PointF Predict(Track t)
    {
        var steps = Math.Min(t.Missed + 1, MaxPredictionFrames);
        return new PointF(t.Center.X + (t.Velocity.X * steps), t.Center.Y + (t.Velocity.Y * steps));
    }

    private static float Gate(Track t) => Math.Min(MaxGate, BaseGate + (GateGrowthPerMissedFrame * t.Missed));

    /// <summary>À profondeur comparable, un combattant garde une taille proche ; une personne bien plus grande ou plus petite est un autre individu.</summary>
    private static bool SimilarSize(Track t, BoundingBox box)
    {
        if (t.Height <= 0 || box.Height <= 0)
        {
            return true;
        }

        var ratio = box.Height / t.Height;
        return ratio is >= 0.6f and <= 1.7f;
    }

    private static void Update(Track t, BoundingBox box)
    {
        var steps = t.Missed + 1;
        var c = box.Center;
        var vx = (c.X - t.Center.X) / steps;
        var vy = (c.Y - t.Center.Y) / steps;
        t.Velocity = new PointF(
            (VelocitySmoothing * t.Velocity.X) + ((1 - VelocitySmoothing) * vx),
            (VelocitySmoothing * t.Velocity.Y) + ((1 - VelocitySmoothing) * vy));
        t.Center = c;
        t.Height = box.Height;
        t.Missed = 0;
    }
}

