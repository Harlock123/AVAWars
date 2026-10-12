using System;
using System.Linq;
using Avalonia;
using AVAWars.Models;

namespace AVAWars.Engine;

/// <summary>Where the demo pilot wants the crosshair, and whether it is firing.</summary>
public readonly record struct PilotOrders(double X, double Y, bool Fire);

/// <summary>
/// Flies the X-wing in attract mode. It swings the crosshair onto the nearest danger (fireballs
/// first, then fighters), works over the ground guns, steers round towers and catwalks, and lines up
/// on the exhaust port. Good enough to put on a show, not good enough to last for ever.
/// </summary>
public class AttractPilot
{
    private static readonly Random Random = new();
    private double _hesitation;
    private Point _wobble;

    public void Reset()
    {
        _hesitation = 0;
    }

    public PilotOrders Fly(GameEngine engine, double deltaTime)
    {
        // Now and then it fumbles for a moment.
        _hesitation -= deltaTime;
        if (_hesitation < -4 && Random.NextDouble() < deltaTime * 0.25)
        {
            _hesitation = 0.4 + Random.NextDouble() * 0.6;
            _wobble = new Point(Random.NextDouble() - 0.5, Random.NextDouble() - 0.5);
        }
        if (_hesitation > 0)
            return new PilotOrders(engine.CrosshairX + _wobble.X * 0.2, engine.CrosshairY + _wobble.Y * 0.2, false);

        var camera = engine.Camera;

        // In the trench, the port comes first.
        if (engine.IsPortInRange)
        {
            var port = camera.ToCamera(engine.PortPosition);
            var at = ToStick(Camera.Project(port));
            bool lined = Distance(engine.CrosshairScreen, Camera.Project(port)) < Camera.ProjectedSize(GameEngine.PortHalfSize, port.Z) + 10;
            return new PilotOrders(at.X, at.Y, lined && engine.Torpedoes.Count == 0 && engine.PortDistance < 330);
        }

        // Things to fly round take priority over things to shoot.
        var dodge = Avoid(engine);
        if (dodge != null)
            return new PilotOrders(dodge.Value.X, dodge.Value.Y, false);

        // The nearest threat: a fireball about to arrive, then fighters, then the ground guns.
        Thing? target = engine.Fireballs
            .Where(f => camera.ToCamera(f.Position).Z is > 60 and < 650)
            .OrderBy(f => camera.ToCamera(f.Position).Z)
            .Cast<Thing>()
            .FirstOrDefault();
        target ??= engine.Ties.Cast<Thing>()
            .Concat(engine.Vader is { IsKnockedOut: false } v ? new Thing[] { v } : Array.Empty<Thing>())
            .Where(t => camera.ToCamera(t.Position).Z > 80)
            .OrderBy(t => camera.ToCamera(t.Position).Z)
            .FirstOrDefault();
        target ??= engine.Bunkers.Cast<Thing>()
            .Concat(engine.Turrets)
            .Concat(engine.Towers.Where(t => t.TopAlive))
            .Where(t => camera.ToCamera(AimPoint(t)).Z is > 150 and < 1300)
            .OrderBy(t => camera.ToCamera(AimPoint(t)).Z)
            .FirstOrDefault();

        if (target == null)
        {
            // Nothing to shoot: drift back toward the middle, and fly level.
            double homeY = engine.Phase == Phase.Surface ? (90 - camera.Position.Y) / 200 : 0;
            return new PilotOrders(-camera.Position.X / 600, homeY, false);
        }

        var c = camera.ToCamera(AimPoint(target));
        var screen = Camera.Project(c);
        var stick = ToStick(screen);
        double reach = Camera.ProjectedSize(target.Radius, c.Z) + 4;
        bool onTarget = Distance(engine.CrosshairScreen, screen) < reach;
        return new PilotOrders(stick.X, stick.Y, onTarget);
    }

    private static Vec3 AimPoint(Thing t) => t switch
    {
        Tower tower => tower.Top,
        Bunker b => b.Position + new Vec3(0, 12, 0),
        _ => t.Position
    };

    /// <summary>A stick position to steer round whatever is about to be hit, or null if nothing is.</summary>
    private static Point? Avoid(GameEngine engine)
    {
        var p = engine.Camera.Position;
        if (engine.Phase == Phase.Surface)
        {
            foreach (var tower in engine.Towers.Where(t => t.Position.Z is > 0 and < 700))
            {
                bool inLine = Math.Abs(tower.Position.X - p.X) < tower.HalfWidth + 30;
                bool low = p.Y < tower.Height + 20;
                if (inLine && low)
                {
                    // Climb over it if that's possible; otherwise go round.
                    if (tower.Height + 25 < GameEngine.SurfaceMaxHeight)
                        return new Point(0, 1);
                    return new Point(tower.Position.X > p.X ? -1 : 1, 0);
                }
            }
        }
        else if (engine.Phase == Phase.Trench)
        {
            foreach (var c in engine.Catwalks.Where(c => c.Position.Z is > 0 and < 600))
            {
                if (Math.Abs(c.Position.Y - p.Y) < 28)
                    return new Point(0, c.Position.Y > -GameEngine.TrenchDepth / 2 ? -1 : 1);
            }
        }
        return null;
    }

    private static Point ToStick(Point screen) => new(
        Math.Clamp((screen.X - Camera.ScreenCentre.X) / 470, -1, 1),
        Math.Clamp((Camera.ScreenCentre.Y - screen.Y) / 330, -1, 1));

    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
