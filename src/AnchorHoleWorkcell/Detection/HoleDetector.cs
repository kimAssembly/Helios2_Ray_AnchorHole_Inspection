using System.Numerics;

namespace AnchorHoleWorkcell.Detection;

public sealed record HoleDetection(int Id, int PixelX, int PixelY, Vector3 Position, float DepthMm, double Confidence, int SupportingPoints);
public readonly record struct SurfaceResidual(int PixelX, int PixelY, float DepthFromPlaneMm);
public sealed record DetectionResult(IReadOnlyList<HoleDetection> Holes, int PlaneInliers, float PlaneRmseMm, float AutomaticThresholdMm, IReadOnlyList<SurfaceResidual> Surface);

public sealed class HoleDetector
{
    public DetectionResult Detect(LiveFrame frame, float mergeRadiusPixels, float planeToleranceMm)
    {
        if (frame.Samples.Count < 50) return new([], 0, 0, 0, []);
        var positions = frame.Samples.Select(point => point.Position).ToArray();
        var plane = FitPlane(positions, planeToleranceMm);
        if (plane.Inliers.Count < 40) return new([], plane.Inliers.Count, plane.Rmse, 0, []);

        var normal = plane.Normal;
        var d = plane.D;
        if (normal.Z < 0) { normal = -normal; d = -d; }

        var inlierErrors = plane.Inliers
            .Select(index => MathF.Abs(Vector3.Dot(normal, positions[index]) + d))
            .OrderBy(value => value).ToArray();
        float median = Median(inlierErrors);
        float mad = Median(inlierErrors.Select(value => MathF.Abs(value - median)).OrderBy(value => value).ToArray());
        float threshold = Math.Max(2, Math.Max(plane.Rmse * 4.5f, median + 6 * mad));
        var residuals = positions.Select(point => Vector3.Dot(normal, point) + d).ToArray();
        var surface = frame.Samples.Select((sample, index) =>
            new SurfaceResidual(sample.PixelX, sample.PixelY, residuals[index])).ToArray();
        var candidates = new List<PeakPoint>();

        for (int index = 0; index < frame.Samples.Count; index++)
        {
            float depth = residuals[index];
            if (depth < threshold || depth > 500) continue;
            if (SurroundedByPlane(frame.Samples, residuals, index, mergeRadiusPixels, planeToleranceMm))
                candidates.Add(new(frame.Samples[index], depth));
        }

        var holes = new List<HoleDetection>();
        foreach (var group in Cluster(candidates, Math.Max(3, mergeRadiusPixels)).OrderByDescending(group => group.Max(point => point.Depth)))
        {
            var deepest = group.OrderByDescending(point => point.Depth)
                .Take(Math.Clamp(group.Count / 2, 1, 10)).ToArray();
            int pixelX = (int)Math.Round(deepest.Average(point => point.Sample.PixelX));
            int pixelY = (int)Math.Round(deepest.Average(point => point.Sample.PixelY));
            var position = new Vector3(
                deepest.Average(point => point.Sample.Position.X),
                deepest.Average(point => point.Sample.Position.Y),
                deepest.Average(point => point.Sample.Position.Z));
            float robustDepth = deepest.Select(point => point.Depth).OrderBy(value => value).ElementAt(deepest.Length / 2);
            double confidence = Math.Clamp(
                .50 + .35 * Math.Min(1, (robustDepth - threshold) / Math.Max(1, threshold)) + .15 * Math.Min(1, group.Count / 5.0), 0, 1);
            holes.Add(new(holes.Count + 1, pixelX, pixelY, position, robustDepth, confidence, group.Count));
        }
        return new(holes, plane.Inliers.Count, plane.Rmse, threshold, surface);
    }

    static bool SurroundedByPlane(IReadOnlyList<LivePoint> samples, float[] residuals, int candidate, float radius, float tolerance)
    {
        float inner = Math.Max(5, radius * .55f), outer = Math.Max(12, radius * 1.6f);
        int neighbors = 0, sectors = 0;
        Span<bool> occupied = stackalloc bool[8];
        var center = samples[candidate];
        for (int index = 0; index < samples.Count; index++)
        {
            float dx = samples[index].PixelX - center.PixelX, dy = samples[index].PixelY - center.PixelY;
            float distanceSquared = dx * dx + dy * dy;
            if (distanceSquared < inner * inner || distanceSquared > outer * outer || MathF.Abs(residuals[index]) > tolerance * 1.5f) continue;
            neighbors++;
            int sector = Math.Min(7, (int)((Math.Atan2(dy, dx) + Math.PI) / (Math.PI * 2) * 8));
            occupied[sector] = true;
        }
        for (int index = 0; index < occupied.Length; index++) if (occupied[index]) sectors++;
        return neighbors >= 6 && sectors >= 4;
    }

    static List<List<PeakPoint>> Cluster(List<PeakPoint> points, float radius)
    {
        var groups = new List<List<PeakPoint>>();
        var used = new bool[points.Count];
        for (int start = 0; start < points.Count; start++)
        {
            if (used[start]) continue;
            var group = new List<PeakPoint>();
            var queue = new Queue<int>();
            queue.Enqueue(start); used[start] = true;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue(); group.Add(points[current]);
                for (int next = 0; next < points.Count; next++)
                {
                    if (used[next]) continue;
                    float dx = points[current].Sample.PixelX - points[next].Sample.PixelX;
                    float dy = points[current].Sample.PixelY - points[next].Sample.PixelY;
                    if (dx * dx + dy * dy > radius * radius) continue;
                    used[next] = true; queue.Enqueue(next);
                }
            }
            groups.Add(group);
        }
        return groups;
    }

    static PlaneFit FitPlane(Vector3[] points, float tolerance)
    {
        var random = new Random(17);
        List<int> best = [];
        Vector3 bestNormal = Vector3.UnitZ;
        float bestD = 0;
        for (int iteration = 0; iteration < 180; iteration++)
        {
            int a = random.Next(points.Length), b = random.Next(points.Length), c = random.Next(points.Length);
            var normal = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
            if (normal.LengthSquared() < 1e-5f) continue;
            normal = Vector3.Normalize(normal);
            float d = -Vector3.Dot(normal, points[a]);
            var inliers = Enumerable.Range(0, points.Length)
                .Where(index => MathF.Abs(Vector3.Dot(normal, points[index]) + d) <= tolerance).ToList();
            if (inliers.Count <= best.Count) continue;
            best = inliers; bestNormal = normal; bestD = d;
        }
        float rmse = best.Count == 0 ? 0 : MathF.Sqrt(best.Average(index => MathF.Pow(Vector3.Dot(bestNormal, points[index]) + bestD, 2)));
        return new(bestNormal, bestD, best, rmse);
    }

    static float Median(float[] sorted) => sorted.Length == 0 ? 0 : sorted.Length % 2 == 1
        ? sorted[sorted.Length / 2]
        : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) * .5f;

    sealed record PeakPoint(LivePoint Sample, float Depth);
    sealed record PlaneFit(Vector3 Normal, float D, List<int> Inliers, float Rmse);
}
