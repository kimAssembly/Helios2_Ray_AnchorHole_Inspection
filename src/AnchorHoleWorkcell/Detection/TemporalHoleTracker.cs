using System.Numerics;

namespace AnchorHoleWorkcell.Detection;

public sealed class TemporalHoleTracker
{
    const int RequiredHits = 3;
    const int MaximumMisses = 3;
    const float PixelGate = 40;
    const float PositionGateMm = 80;
    const float Alpha = .30f;
    readonly List<Track> tracks = [];
    int nextId = 1;

    public IReadOnlyList<HoleDetection> Update(IReadOnlyList<HoleDetection> detections)
    {
        foreach (var track in tracks) track.Matched = false;

        foreach (var detection in detections.OrderByDescending(item => item.Confidence))
        {
            var match = tracks
                .Where(track => !track.Matched && PixelDistance(track, detection) <= PixelGate &&
                                Vector3.Distance(track.Position, detection.Position) <= PositionGateMm)
                .OrderBy(track => PixelDistance(track, detection))
                .FirstOrDefault();

            if (match is null)
            {
                tracks.Add(new Track(nextId++, detection));
                continue;
            }

            match.PixelX = Lerp(match.PixelX, detection.PixelX, Alpha);
            match.PixelY = Lerp(match.PixelY, detection.PixelY, Alpha);
            match.Position = Vector3.Lerp(match.Position, detection.Position, Alpha);
            match.DepthMm = Lerp(match.DepthMm, detection.DepthMm, Alpha);
            match.Confidence = match.Confidence * (1 - Alpha) + detection.Confidence * Alpha;
            match.SupportingPoints = detection.SupportingPoints;
            match.Hits++;
            match.Misses = 0;
            match.Matched = true;
        }

        foreach (var track in tracks.Where(item => !item.Matched)) track.Misses++;
        tracks.RemoveAll(track => track.Misses > MaximumMisses);

        return tracks.Where(track => track.Hits >= RequiredHits && track.Misses == 0)
            .Select(track => new HoleDetection(track.Id, (int)Math.Round(track.PixelX), (int)Math.Round(track.PixelY),
                track.Position, track.DepthMm, track.Confidence, track.SupportingPoints))
            .ToArray();
    }

    public void Reset()
    {
        tracks.Clear();
        nextId = 1;
    }

    static float PixelDistance(Track track, HoleDetection detection)
    {
        float x = track.PixelX - detection.PixelX, y = track.PixelY - detection.PixelY;
        return MathF.Sqrt(x * x + y * y);
    }

    static float Lerp(float from, float to, float amount) => from + (to - from) * amount;

    sealed class Track
    {
        public Track(int id, HoleDetection detection)
        {
            Id = id; PixelX = detection.PixelX; PixelY = detection.PixelY;
            Position = detection.Position; DepthMm = detection.DepthMm;
            Confidence = detection.Confidence; SupportingPoints = detection.SupportingPoints;
            Hits = 1; Matched = true;
        }

        public int Id { get; }
        public float PixelX { get; set; }
        public float PixelY { get; set; }
        public Vector3 Position { get; set; }
        public float DepthMm { get; set; }
        public double Confidence { get; set; }
        public int SupportingPoints { get; set; }
        public int Hits { get; set; }
        public int Misses { get; set; }
        public bool Matched { get; set; }
    }
}
