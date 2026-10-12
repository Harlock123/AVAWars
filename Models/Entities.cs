using System;

namespace AVAWars.Models;

/// <summary>Anything out there in 3D: a position, a speed, and a size for hits and collisions.</summary>
public abstract class Thing
{
    public Vec3 Position { get; set; }
    public Vec3 Velocity { get; set; }
    public double Radius { get; protected init; }
    public bool IsAlive { get; set; } = true;

    /// <summary>A short flash after a hit that didn't destroy it.</summary>
    public double HitFlash { get; set; }

    public virtual void Update(double deltaTime)
    {
        Position += Velocity * deltaTime;
        HitFlash = Math.Max(0, HitFlash - deltaTime);
    }
}

/// <summary>An enemy fighter. TIEs swoop in from ahead, fire as they close, and break off past the ship.</summary>
public class TieFighter : Thing
{
    public const int Points = 1000;

    public double Yaw { get; set; }
    public double Bank { get; set; }
    public double FireTimer { get; set; }
    public double Weave { get; set; }

    /// <summary>The point it is making for, relative to the ship, before it breaks away.</summary>
    public Vec3 PassPoint { get; set; }
    public bool Breaking { get; set; }

    public TieFighter(Vec3 position)
    {
        Position = position;
        Radius = 26;
    }
}

/// <summary>
/// Darth Vader's TIE, waiting in the trench. It sits ahead of the ship, slips from side to side and
/// fires often. It can't be destroyed, but enough hits send it spinning out of the trench.
/// </summary>
public class VaderTie : Thing
{
    public const int PointsPerHit = 2000;

    public int HitsLeft { get; set; }
    public double FireTimer { get; set; }
    public double Weave { get; set; }

    /// <summary>Seconds since it was knocked away, or -1 while it is still fighting.</summary>
    public double SpinOut { get; set; } = -1;
    public bool IsKnockedOut => SpinOut >= 0;

    public VaderTie(Vec3 position, int hits)
    {
        Position = position;
        Radius = 30;
        HitsLeft = hits;
    }
}

/// <summary>An enemy shot: a spinning ball of fire that flies at the ship. It can be shot down.</summary>
public class Fireball : Thing
{
    public const int Points = 33;
    public double Spin { get; set; }

    public Fireball(Vec3 position, Vec3 velocity)
    {
        Position = position;
        Velocity = velocity;
        Radius = 9;
    }

    public override void Update(double deltaTime)
    {
        Spin += deltaTime * 9;
        base.Update(deltaTime);
    }
}

/// <summary>A laser bunker on the Death Star's surface.</summary>
public class Bunker : Thing
{
    public const int Points = 200;
    public double FireTimer { get; set; }

    public Bunker(Vec3 position)
    {
        Position = position;
        Radius = 20;
    }
}

/// <summary>
/// A laser tower: a tall block to fly round or over, with a gun on top that can be shot off. Each
/// tower top is worth more than the last, and all of them together earn a big bonus.
/// </summary>
public class Tower : Thing
{
    public const int AllTopsBonus = 50000;

    public double Height { get; }
    public double HalfWidth { get; }
    public bool TopAlive { get; set; } = true;
    public double FireTimer { get; set; }

    public Vec3 Top => Position + new Vec3(0, Height + 8, 0);

    public Tower(Vec3 basePosition, double height, double halfWidth)
    {
        Position = basePosition;
        Height = height;
        HalfWidth = halfWidth;
        Radius = 14;
    }
}

/// <summary>A gun turret on the trench wall or floor.</summary>
public class Turret : Thing
{
    public const int Points = 100;
    public double FireTimer { get; set; }

    /// <summary>Which way it faces: -1 on the right wall, +1 on the left, 0 on the floor.</summary>
    public int Facing { get; }

    public Turret(Vec3 position, int facing)
    {
        Position = position;
        Facing = facing;
        Radius = 15;
    }
}

/// <summary>A catwalk across the trench, to be flown over or under.</summary>
public class Catwalk : Thing
{
    public const double HalfThickness = 7;

    public Catwalk(Vec3 position)
    {
        Position = position;
        Radius = HalfThickness;
    }
}

/// <summary>A proton torpedo on its way to the exhaust port.</summary>
public class Torpedo : Thing
{
    public double Age { get; set; }
    public double FlightTime { get; }
    public Vec3 Start { get; }
    public Vec3 Target { get; }
    public bool OnTarget { get; }

    public Torpedo(Vec3 start, Vec3 target, double flightTime, bool onTarget)
    {
        Start = start;
        Target = target;
        Position = start;
        FlightTime = flightTime;
        OnTarget = onTarget;
        Radius = 4;
    }

    public double Progress => Math.Min(1, Age / FlightTime);
}

/// <summary>A burst of the X-wing's four cannons, converging on the crosshair.</summary>
public class LaserShot
{
    public const double ShowTime = 0.13;

    public Avalonia.Point Target { get; }
    public double Age { get; set; }
    public bool Hit { get; }

    public LaserShot(Avalonia.Point target, bool hit)
    {
        Target = target;
        Hit = hit;
    }
}
