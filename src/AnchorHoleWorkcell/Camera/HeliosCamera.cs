using ArenaNET_MP;
using System.Numerics;
using System.Runtime.InteropServices;

namespace AnchorHoleWorkcell;

public readonly record struct LivePoint(Vector3 Position, int PixelX, int PixelY);
public sealed record LiveFrame(int Width, int Height, byte[] Bgra, IReadOnlyList<LivePoint> Samples, long FrameId);

public sealed class HeliosCamera : IAsyncDisposable
{
    const string ArenaBin = @"C:\Program Files\LUCID Vision Labs\Arena SDK\x64Release";
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string? path);

    CancellationTokenSource? cancellation;
    Task? worker;
    TaskCompletionSource? firstFrame;
    float minimumZ = 200, maximumZ = 1600;
    float roiX, roiY, roiWidth = 1, roiHeight = 1;

    public bool IsRunning => worker is { IsCompleted: false };
    public event Action<LiveFrame>? FrameReady;
    public event Action<string>? StatusChanged;

    public async Task StartAsync()
    {
        if (IsRunning) return;
        if (cancellation is not null)
        {
            cancellation.Dispose(); cancellation = null; worker = null;
        }
        cancellation = new();
        firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
        worker = Task.Run(() => Acquire(cancellation.Token));
        await Task.WhenAny(firstFrame.Task, worker);
        if (!firstFrame.Task.IsCompletedSuccessfully)
            throw new InvalidOperationException("Camera stopped before the first frame. Close ArenaView and verify the camera/network connection.");
    }

    public async Task StopAsync()
    {
        if (cancellation is null) return;
        cancellation.Cancel();
        if (worker is not null) try { await worker; } catch (OperationCanceledException) { }
        cancellation.Dispose(); cancellation = null; worker = null;
    }

    public void SetDepthRange(float minimumMm, float maximumMm)
    {
        minimumZ = minimumMm; maximumZ = maximumMm;
        StatusChanged?.Invoke($"Z RANGE · {minimumMm:F0}–{maximumMm:F0} mm");
    }

    public void SetRoi(float x, float y, float width, float height)
    {
        roiX = Math.Clamp(x, 0, 1); roiY = Math.Clamp(y, 0, 1);
        roiWidth = Math.Clamp(width, 0, 1 - roiX); roiHeight = Math.Clamp(height, 0, 1 - roiY);
    }

    void Acquire(CancellationToken token)
    {
        SetDllDirectory(ArenaBin);
        ISystem? system = null; IDevice? device = null; bool streaming = false;
        try
        {
            system = ArenaNET_MP.ArenaNET_MP.OpenSystem();
            system.UpdateDevices(700);
            if (system.Devices.Count == 0) throw new InvalidOperationException("연결된 LUCID 카메라가 없습니다.");
            var info = system.Devices.FirstOrDefault(item => item.ModelName.Contains("HTR", StringComparison.OrdinalIgnoreCase)) ?? system.Devices[0];
            if (info.IpAddressStr.StartsWith("169.254.", StringComparison.Ordinal))
            {
                StatusChanged?.Invoke($"CAMERA IP RECOVERY | {info.IpAddressStr} -> 192.168.0.41");
                system.ForceIp(info.MacAddress, ToIpv4("192.168.0.41"), ToIpv4("255.255.255.0"), ToIpv4("0.0.0.0"));
                Thread.Sleep(500);
                string serial = info.SerialNumber;
                system.UpdateDevices(1200);
                info = system.Devices.FirstOrDefault(item => item.SerialNumber == serial)
                    ?? throw new InvalidOperationException("Camera did not reappear after Force-IP.");
            }
            device = system.CreateDevice(info);
            dynamic camera = ArenaNET_MP.ArenaNET_MP.AsDynamic(device);
            camera.TLStreamNodeMap.StreamAutoNegotiatePacketSize.Value = true;
            camera.TLStreamNodeMap.StreamPacketResendEnable.Value = true;
            camera.PixelFormat.Value = "Coord3D_ABCY16";
            float scale = (float)camera.Scan3dCoordinateScale.Value;
            camera.Scan3dCoordinateSelector.Value = "CoordinateA"; float offsetX = (float)camera.Scan3dCoordinateOffset.Value;
            camera.Scan3dCoordinateSelector.Value = "CoordinateB"; float offsetY = (float)camera.Scan3dCoordinateOffset.Value;
            camera.Scan3dCoordinateSelector.Value = "CoordinateC"; float offsetZ = (float)camera.Scan3dCoordinateOffset.Value;
            device.StartStream(); streaming = true;
            StatusChanged?.Invoke($"CAMERA CONNECTED · {info.ModelName} · {info.IpAddressStr}");
            long frameId = 0;
            while (!token.IsCancellationRequested)
            {
                IImage? image = null;
                try
                {
                    image = device.GetImage(1000);
                    Publish(image, scale, offsetX, offsetY, offsetZ, ++frameId);
                }
                finally { if (image is not null) device.RequeueBuffer(image); }
            }
        }
        catch (Exception exception) { StatusChanged?.Invoke("CAMERA ERROR · " + exception.Message); }
        finally
        {
            if (device is not null && streaming) try { device.StopStream(); } catch { }
            if (system is not null && device is not null) try { system.DestroyDevice(device); } catch { }
            if (system is not null) try { ArenaNET_MP.ArenaNET_MP.CloseSystem(system); } catch { }
            StatusChanged?.Invoke("CAMERA STOPPED");
        }
    }

    void Publish(IImage image, float scale, float offsetX, float offsetY, float offsetZ, long frameId)
    {
        byte[] data = image.DataArray;
        int width = (int)image.Width, height = (int)image.Height;
        float min = minimumZ, max = maximumZ;
        var pixels = new byte[width * height * 4];
        for (int source = 4, target = 0; target < pixels.Length; source += 8, target += 4)
        {
            var color = Heat(BitConverter.ToUInt16(data, source) * scale + offsetZ, min, max);
            pixels[target] = color.B; pixels[target + 1] = color.G; pixels[target + 2] = color.R; pixels[target + 3] = 255;
        }
        int x0 = Math.Clamp((int)(roiX * width), 0, width - 1), x1 = Math.Clamp((int)Math.Ceiling((roiX + roiWidth) * width), x0 + 1, width);
        int y0 = Math.Clamp((int)(roiY * height), 0, height - 1), y1 = Math.Clamp((int)Math.Ceiling((roiY + roiHeight) * height), y0 + 1, height);
        var samples = new List<LivePoint>();
        for (int y = y0; y < y1; y += 3)
        for (int x = x0; x < x1; x += 3)
        {
            int index = (y * width + x) * 8;
            float z = BitConverter.ToUInt16(data, index + 4) * scale + offsetZ;
            if (z < min || z > max) continue;
            samples.Add(new(new(
                BitConverter.ToUInt16(data, index) * scale + offsetX,
                BitConverter.ToUInt16(data, index + 2) * scale + offsetY, z), x, y));
        }
        FrameReady?.Invoke(new(width, height, pixels, samples, frameId));
        firstFrame?.TrySetResult();
    }

    static (byte R, byte G, byte B) Heat(float z, float min, float max)
    {
        if (z < min || z > max) return (0, 0, 0);
        float t = Math.Clamp((z - min) / Math.Max(1, max - min), 0, 1);
        return ((byte)(255 * Math.Clamp(1.5f - Math.Abs(4 * t - 3), 0, 1)),
                (byte)(255 * Math.Clamp(1.5f - Math.Abs(4 * t - 2), 0, 1)),
                (byte)(255 * Math.Clamp(1.5f - Math.Abs(4 * t - 1), 0, 1)));
    }

    static uint ToIpv4(string address)
    {
        var bytes = System.Net.IPAddress.Parse(address).GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
