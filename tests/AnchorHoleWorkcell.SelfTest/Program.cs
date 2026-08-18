using AnchorHoleWorkcell;
using AnchorHoleWorkcell.Detection;

const int width = 640, height = 480, holeX = 320, holeY = 240;
const float surfaceZ = 800, mmPerPixel = 1.65f;
var samples = new List<LivePoint>();
var random = new Random(11);
for (int y = 90; y <= 390; y += 3)
for (int x = 100; x <= 540; x += 3)
{
    float noise = ((float)random.NextDouble() - .5f) * 1.2f;
    float depth = (x - holeX) * (x - holeX) + (y - holeY) * (y - holeY) <= 64 ? 38 : 0;
    samples.Add(new(new((x - 320) * mmPerPixel, (y - 240) * mmPerPixel, surfaceZ + depth + noise), x, y));
}
var result = new HoleDetector().Detect(new(width, height, new byte[width * height * 4], samples, 1), 18, 3);
foreach (var hole in result.Holes) Console.WriteLine($"#{hole.Id} depth={hole.DepthMm:F1}mm confidence={hole.Confidence:P1} XYZ={hole.Position}");
return result.Holes.Any(hole => Math.Abs(hole.PixelX - holeX) < 10 && Math.Abs(hole.PixelY - holeY) < 10 && hole.DepthMm > 30) ? 0 : 1;
