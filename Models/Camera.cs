using System;
using Avalonia;

namespace AVAWars.Models;

/// <summary>
/// The view out of the cockpit: where the ship is, how far it has banked, and how points in front
/// of it land on the 1024×768 screen. The game uses it to decide what the crosshair is over, and the
/// screen uses it to draw, so the two always agree.
/// </summary>
public class Camera
{
    public const double ScreenWidth = 1024;
    public const double ScreenHeight = 768;
    public const double Focal = 520;
    public const double Near = 4;
    public static readonly Point ScreenCentre = new(ScreenWidth / 2, ScreenHeight / 2);

    /// <summary>The ship's position in the world. Zero in space, where the world turns round the ship instead.</summary>
    public Vec3 Position { get; set; }

    /// <summary>Bank angle, for the look of turning. Positive rolls the horizon clockwise.</summary>
    public double Roll { get; set; }

    public Vec3 ToCamera(Vec3 world) => (world - Position).RotateZ(Roll);

    public static Point Project(Vec3 camera) =>
        new(ScreenCentre.X + camera.X / camera.Z * Focal, ScreenCentre.Y - camera.Y / camera.Z * Focal);

    public static double ProjectedSize(double size, double depth) => size / Math.Max(depth, Near) * Focal;

    /// <summary>Where the crosshair sits on screen for a stick position from -1 to 1 on each axis.</summary>
    public static Point CrosshairToScreen(double x, double y) =>
        new(ScreenCentre.X + x * 470, ScreenCentre.Y - y * 330);
}
