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
