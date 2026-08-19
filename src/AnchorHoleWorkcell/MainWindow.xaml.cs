using AnchorHoleWorkcell.Detection;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AnchorHoleWorkcell;

public partial class MainWindow : Window
{
    readonly HeliosCamera camera = new();
    readonly HoleDetector detector = new();
    readonly TemporalHoleTracker tracker = new();
    readonly ObservableCollection<HoleRow> rows = [];
    bool inspecting, drawing;
    int detectionBusy;
    long lastDetection;
    string? cameraError;
    float detectionRadius = 18;
    float planeTolerance = 4;
    DetectionResult? lastResult;
    IReadOnlyList<HoleDetection> lastStableHoles = [];
    Point dragStart;
    Rect roi = new(0, 0, 1, 1);

    public MainWindow()
    {
        InitializeComponent();
        LiveButton.Click -= Live_Click;
        LiveButton.Click += SafeLive_Click;
        ResultGrid.ItemsSource = rows;
        camera.FrameReady += OnFrame;
        camera.StatusChanged += status => Dispatcher.InvokeAsync(() =>
        {
            if (status.StartsWith("CAMERA ERROR", StringComparison.Ordinal)) cameraError = status;
            Footer.Text = status;
        });
        Closed += async (_, _) => await camera.StopAsync();
    }

    async void Live_Click(object sender, RoutedEventArgs e)
    {
        if (camera.IsRunning)
        {
            inspecting = false; InspectButton.IsChecked = false;
            await camera.StopAsync(); LiveButton.Content = "▶ LIVE START";
        }
        else
        {
            await camera.StartAsync(); LiveButton.Content = "■ LIVE STOP";
            EmptyLabel.Visibility = Visibility.Collapsed;
        }
    }

    void Inspect_Click(object sender, RoutedEventArgs e)
    {
        if (InspectButton.IsChecked == true && !camera.IsRunning)
        {
            InspectButton.IsChecked = false;
            MessageBox.Show("먼저 LIVE START를 눌러주세요.");
            return;
        }
        if (InspectButton.IsChecked == true &&
            (!float.TryParse(MergeRadius.Text, out detectionRadius) ||
             !float.TryParse(PlaneTolerance.Text, out planeTolerance)))
        {
            InspectButton.IsChecked = false;
            MessageBox.Show("Detection settings must be numeric.");
            return;
        }
        inspecting = InspectButton.IsChecked == true;
        tracker.Reset();
        StateText.Text = inspecting ? "INSPECTING" : "IDLE";
    }

    void Range_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (RangeBox.SelectedItem is not ComboBoxItem { Tag: string value }) return;
        var limits = value.Split(',');
        camera.SetDepthRange(float.Parse(limits[0]), float.Parse(limits[1]));
    }

    void OnFrame(LiveFrame frame)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Bgra, frame.Width * 4, 0);
            Heatmap.Source = bitmap;
        });
        if (!inspecting || frame.Samples.Count < 80 || Environment.TickCount64 - lastDetection < 350 || Interlocked.CompareExchange(ref detectionBusy, 1, 0) != 0) return;
        lastDetection = Environment.TickCount64;
        float radius = detectionRadius;
        float tolerance = planeTolerance;
        _ = Task.Run(() => detector.Detect(frame, radius, tolerance)).ContinueWith(task => Dispatcher.InvokeAsync(() =>
        {
            try { if (task.IsCompletedSuccessfully) Render(task.Result, frame); }
            finally { Interlocked.Exchange(ref detectionBusy, 0); }
        }));
    }

    void Render(DetectionResult result, LiveFrame frame)
    {
        var stableHoles = tracker.Update(result.Holes);
        UpdateGraphData(result, stableHoles);
        HoleLayer.Width = VideoHost.ActualWidth; HoleLayer.Height = VideoHost.ActualHeight;
        HoleLayer.Children.Clear(); rows.Clear();
        foreach (var hole in stableHoles)
        {
            double x = (double)hole.PixelX / frame.Width * VideoHost.ActualWidth;
            double y = (double)hole.PixelY / frame.Height * VideoHost.ActualHeight;
            double diameter = Math.Clamp(16 + hole.SupportingPoints * 2, 18, 64);
            var marker = new Ellipse { Width = diameter, Height = diameter, Stroke = Brushes.Lime, StrokeThickness = 3, Fill = new SolidColorBrush(Color.FromArgb(35, 0, 255, 0)) };
            Canvas.SetLeft(marker, x - diameter / 2); Canvas.SetTop(marker, y - diameter / 2); HoleLayer.Children.Add(marker);
            var label = new Border { Background = Brushes.Black, Padding = new Thickness(5, 2, 5, 2), Child = new TextBlock { Text = $"#{hole.Id} {hole.DepthMm:F1} mm", Foreground = Brushes.White } };
            Canvas.SetLeft(label, x + diameter / 2); Canvas.SetTop(label, y - diameter / 2); HoleLayer.Children.Add(label);
            rows.Add(new(hole.Id, $"{hole.Confidence:P0}", $"{hole.DepthMm:F1} mm", $"{hole.Position.X:F1}", $"{hole.Position.Y:F1}", $"{hole.Position.Z:F1}"));
        }
        StateText.Text = stableHoles.Count > 0 ? $"FOUND {stableHoles.Count} HOLE(S)" : "VERIFYING...";
        Footer.Text = $"Plane inliers {result.PlaneInliers:N0} · RMSE {result.PlaneRmseMm:F2} mm · auto threshold {result.AutomaticThresholdMm:F2} mm";
    }

    void UpdateGraphData(DetectionResult result, IReadOnlyList<HoleDetection> stableHoles)
    {
        lastResult = result;
        lastStableHoles = stableHoles;
        int? selected = HoleSelector.SelectedItem is int id ? id : null;
        var ids = stableHoles.Select(hole => hole.Id).ToArray();
        HoleSelector.ItemsSource = ids;
        HoleSelector.SelectedItem = selected is int selectedId && ids.Contains(selectedId)
            ? selectedId
            : ids.Length > 0 ? ids[0] : null;
        DrawNoiseGraph();
    }

    void HoleSelector_Changed(object sender, SelectionChangedEventArgs e) => DrawNoiseGraph();
    void NoiseGraph_SizeChanged(object sender, SizeChangedEventArgs e) => DrawNoiseGraph();

    void DrawNoiseGraph()
    {
        NoiseGraph.Children.Clear();
        double width = NoiseGraph.ActualWidth, height = NoiseGraph.ActualHeight;
        if (width < 100 || height < 80 || lastResult is null || HoleSelector.SelectedItem is not int selectedId)
        {
            GraphSummary.Text = "Select a detected hole";
            return;
        }

        var hole = lastStableHoles.FirstOrDefault(item => item.Id == selectedId);
        if (hole is null) return;
        int halfBand = Math.Max(5, (int)Math.Round(detectionRadius * .45f));
        var profile = lastResult.Surface
            .Where(sample => Math.Abs(sample.PixelY - hole.PixelY) <= halfBand && Math.Abs(sample.DepthFromPlaneMm) < 500)
            .GroupBy(sample => sample.PixelX)
            .Select(group => new
            {
                X = group.Key,
                Depth = ProfileMedian(group.Select(sample => sample.DepthFromPlaneMm).OrderBy(value => value).ToArray())
            })
            .OrderBy(point => point.X).ToArray();

        if (profile.Length < 2)
        {
            GraphSummary.Text = "Not enough surface points";
            return;
        }

        const double left = 44, right = 12, top = 18, bottom = 27;
        double plotWidth = Math.Max(1, width - left - right), plotHeight = Math.Max(1, height - top - bottom);
        float maximum = Math.Max(10, Math.Max(hole.DepthMm * 1.25f, lastResult.AutomaticThresholdMm * 2.2f));
        float minimum = -Math.Max(3, maximum * .15f);
        int minimumX = profile[0].X, maximumX = profile[^1].X;
        double MapX(int x) => left + (x - minimumX) / (double)Math.Max(1, maximumX - minimumX) * plotWidth;
        double MapY(float depth) => top + (maximum - Math.Clamp(depth, minimum, maximum)) / (maximum - minimum) * plotHeight;

        AddGraphLine(left, MapY(0), width - right, MapY(0), "#526173", 1);
        AddGraphLine(left, MapY(lastResult.AutomaticThresholdMm), width - right, MapY(lastResult.AutomaticThresholdMm), "#FFD54A", 1, [5, 4]);
        AddGraphLine(MapX(hole.PixelX), top, MapX(hole.PixelX), height - bottom, "#FF7043", 1, [3, 3]);

        var curve = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(67, 181, 255)), StrokeThickness = 2 };
        foreach (var point in profile)
        {
            double x = MapX(point.X), y = MapY(point.Depth);
            curve.Points.Add(new Point(x, y));
            if (point.Depth < lastResult.AutomaticThresholdMm) continue;
            var peak = new Ellipse { Width = 5, Height = 5, Fill = Brushes.OrangeRed };
            Canvas.SetLeft(peak, x - 2.5); Canvas.SetTop(peak, y - 2.5); NoiseGraph.Children.Add(peak);
        }
        NoiseGraph.Children.Insert(0, curve);

        AddGraphText("0", 8, MapY(0) - 9, "#9AA8B8");
        AddGraphText($"{lastResult.AutomaticThresholdMm:F1}", 3, MapY(lastResult.AutomaticThresholdMm) - 9, "#FFD54A");
        AddGraphText($"{maximum:F0} mm", 3, top - 8, "#9AA8B8");
        AddGraphText("surface noise", left + 5, MapY(0) + 3, "#7F8D9D");
        AddGraphText("noise peak", Math.Min(width - 75, MapX(hole.PixelX) + 5), top + 2, "#FF7043");
        GraphSummary.Text = $"Peak {hole.DepthMm:F1} mm · threshold {lastResult.AutomaticThresholdMm:F1} mm";
    }

    void AddGraphLine(double x1, double y1, double x2, double y2, string color, double thickness, double[]? dash = null)
    {
        var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = (Brush)new BrushConverter().ConvertFromString(color)!, StrokeThickness = thickness };
        if (dash is not null) line.StrokeDashArray = new DoubleCollection(dash);
        NoiseGraph.Children.Add(line);
    }

    void AddGraphText(string text, double x, double y, string color)
    {
        var label = new TextBlock { Text = text, Foreground = (Brush)new BrushConverter().ConvertFromString(color)!, FontSize = 11 };
        Canvas.SetLeft(label, x); Canvas.SetTop(label, y); NoiseGraph.Children.Add(label);
    }

    static float ProfileMedian(float[] sorted) => sorted.Length == 0 ? 0 : sorted.Length % 2 == 1
        ? sorted[sorted.Length / 2]
        : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) * .5f;

    void Video_MouseDown(object sender, MouseButtonEventArgs e) { drawing = true; dragStart = e.GetPosition(VideoHost); VideoHost.CaptureMouse(); }
    void Video_MouseMove(object sender, MouseEventArgs e) { if (drawing) DrawRoi(dragStart, e.GetPosition(VideoHost), false); }
    void Video_MouseUp(object sender, MouseButtonEventArgs e) { if (!drawing) return; drawing = false; VideoHost.ReleaseMouseCapture(); DrawRoi(dragStart, e.GetPosition(VideoHost), true); }
    void DrawRoi(Point a, Point b, bool commit)
    {
        double width = Math.Max(1, VideoHost.ActualWidth), height = Math.Max(1, VideoHost.ActualHeight);
        double x = Math.Clamp(Math.Min(a.X, b.X), 0, width), y = Math.Clamp(Math.Min(a.Y, b.Y), 0, height);
        double roiWidth = Math.Min(Math.Abs(a.X - b.X), width - x), roiHeight = Math.Min(Math.Abs(a.Y - b.Y), height - y);
        Canvas.SetLeft(RoiBox, x); Canvas.SetTop(RoiBox, y); RoiBox.Width = roiWidth; RoiBox.Height = roiHeight;
        if (!commit || roiWidth <= 8 || roiHeight <= 8) return;
        tracker.Reset();
        roi = new(x / width, y / height, roiWidth / width, roiHeight / height);
        camera.SetRoi((float)roi.X, (float)roi.Y, (float)roi.Width, (float)roi.Height);
    }
    void ResetRoi_Click(object sender, RoutedEventArgs e) { roi = new(0, 0, 1, 1); tracker.Reset(); camera.SetRoi(0, 0, 1, 1); LayoutRoi(); }
    void Video_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutRoi();
    void LayoutRoi() { Canvas.SetLeft(RoiBox, roi.X * VideoHost.ActualWidth); Canvas.SetTop(RoiBox, roi.Y * VideoHost.ActualHeight); RoiBox.Width = roi.Width * VideoHost.ActualWidth; RoiBox.Height = roi.Height * VideoHost.ActualHeight; }

    async void SafeLive_Click(object sender, RoutedEventArgs e)
    {
        if (camera.IsRunning)
        {
            inspecting = false;
            InspectButton.IsChecked = false;
            await camera.StopAsync();
            LiveButton.Content = "LIVE START";
            return;
        }

        LiveButton.IsEnabled = false;
        cameraError = null;
        Footer.Text = "CAMERA CONNECTING...";
        try
        {
            await camera.StartAsync();
            LiveButton.Content = "LIVE STOP";
            EmptyLabel.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            string message = cameraError ?? exception.Message;
            LiveButton.Content = "LIVE START";
            EmptyLabel.Visibility = Visibility.Visible;
            Footer.Text = message;
            MessageBox.Show(message, "Helios camera connection failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { LiveButton.IsEnabled = true; }
    }
}

public sealed record HoleRow(int Id, string Confidence, string Depth, string X, string Y, string Z);
