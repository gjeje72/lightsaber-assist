using lightsaber_assist.Core.Analysis;
using lightsaber_assist.Vision;

namespace lightsaber_assist.Tests;

public class FighterTrackerTests
{
    private static (BoundingBox Box, Skeleton Skeleton) Det(float cx, float bottom = 400, float height = 200) =>
        (new BoundingBox(cx - 40, bottom - height, 80, height, 0.9f), new Skeleton());

    private static readonly PointF[] Seeds = [new(100, 300), new(700, 300)];

    [Fact]
    public void Seeded_IgnoresBystanderWhenFighterIsHidden()
    {
        var tracker = new FighterTracker();
        tracker.Seed(Seeds);

        var first = tracker.Assign([Det(100), Det(700), Det(400)]);
        Assert.Equal([0, 1], first.Select(f => f.Id).ToArray());

        // Le combattant 1 disparaît ; l'arbitre au centre ne doit pas hériter de son identifiant.
        var second = tracker.Assign([Det(105), Det(400)]);
        Assert.Single(second);
        Assert.Equal(0, second[0].Id);
    }

    [Fact]
    public void Seeded_ReacquiresAfterLongLoss_WithinAllowedArea()
    {
        var tracker = new FighterTracker();
        tracker.Seed(Seeds);
        tracker.Assign([Det(100), Det(700)]);

        for (var i = 0; i < FighterTracker.ReacquireAfterFrames; i++)
        {
            tracker.Assign([Det(100)]);
        }

        var result = tracker.Assign([Det(100), Det(650)]);
        Assert.Equal([0, 1], result.Select(f => f.Id).ToArray());
    }

    [Fact]
    public void Unseeded_KeepsAtMostTwoStableIds()
    {
        var tracker = new FighterTracker();
        var a = tracker.Assign([Det(100), Det(700), Det(400)]);
        Assert.Equal(2, a.Count);

        var b = tracker.Assign([Det(110), Det(690), Det(400)]);
        Assert.Equal(2, b.Count);
        Assert.Equal(a.Select(x => x.Id).Order(), b.Select(x => x.Id).Order());
    }

    [Fact]
    public void CanStartTrack_FiltersNewTracksOnly()
    {
        var tracker = new FighterTracker();
        var first = tracker.Assign([Det(100), Det(700)], b => b.Center.X < 120);
        Assert.Single(first);

        // La piste existante reste prolongée même hors du filtre.
        var second = tracker.Assign([Det(150)], b => b.Center.X < 120);
        Assert.Single(second);
        Assert.Equal(first[0].Id, second[0].Id);
    }
}
