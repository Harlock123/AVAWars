using System;
using System.Collections.Generic;

namespace AVAWars.Models;

/// <summary>
/// A vector-display model: nothing but line segments in 3D, about a unit in size, nose along +Z.
/// </summary>
public class Wireframe
{
    public (Vec3 A, Vec3 B)[] Lines { get; }

    private Wireframe(List<(Vec3, Vec3)> lines) => Lines = lines.ToArray();

    private sealed class Builder
    {
        private readonly List<(Vec3, Vec3)> _lines = new();

        public Builder Line(Vec3 a, Vec3 b)
        {
            _lines.Add((a, b));
            return this;
        }

        public Builder Path(bool closed, params Vec3[] points)
        {
            for (int i = 0; i < points.Length - 1; i++)
                _lines.Add((points[i], points[i + 1]));
            if (closed && points.Length > 2)
                _lines.Add((points[^1], points[0]));
            return this;
        }

        /// <summary>A ring of points in the plane facing forward (X and Y vary).</summary>
        public static Vec3[] FrontRing(Vec3 c, double r, int count, double phase = 0)
        {
            var points = new Vec3[count];
            for (int i = 0; i < count; i++)
            {
                double a = phase + i * Math.PI * 2 / count;
                points[i] = new Vec3(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r, c.Z);
            }
            return points;
        }

        /// <summary>A ring of points in the plane facing sideways (Y and Z vary).</summary>
        public static Vec3[] SideRing(Vec3 c, double r, int count, double phase = 0)
        {
            var points = new Vec3[count];
            for (int i = 0; i < count; i++)
            {
                double a = phase + i * Math.PI * 2 / count;
                points[i] = new Vec3(c.X, c.Y + Math.Sin(a) * r, c.Z + Math.Cos(a) * r);
            }
            return points;
        }

        /// <summary>A ring of points in the level plane (X and Z vary).</summary>
        public static Vec3[] FlatRing(Vec3 c, double r, int count, double phase = 0)
        {
            var points = new Vec3[count];
            for (int i = 0; i < count; i++)
            {
                double a = phase + i * Math.PI * 2 / count;
                points[i] = new Vec3(c.X + Math.Cos(a) * r, c.Y, c.Z + Math.Sin(a) * r);
            }
            return points;
        }

        /// <summary>A tube along Z.</summary>
        public Builder Tube(double x, double y, double z0, double z1, double r, int sides)
        {
            var front = FrontRing(new Vec3(x, y, z0), r, sides, Math.PI / sides);
            var back = FrontRing(new Vec3(x, y, z1), r, sides, Math.PI / sides);
            Path(true, front).Path(true, back);
            for (int i = 0; i < sides; i++)
                Line(front[i], back[i]);
            return this;
        }

        public Builder Box(Vec3 min, Vec3 max)
        {
            var p = new[]
            {
                new Vec3(min.X, min.Y, min.Z), new Vec3(max.X, min.Y, min.Z), new Vec3(max.X, min.Y, max.Z), new Vec3(min.X, min.Y, max.Z),
                new Vec3(min.X, max.Y, min.Z), new Vec3(max.X, max.Y, min.Z), new Vec3(max.X, max.Y, max.Z), new Vec3(min.X, max.Y, max.Z)
            };
            Path(true, p[0], p[1], p[2], p[3]).Path(true, p[4], p[5], p[6], p[7]);
            for (int i = 0; i < 4; i++)
                Line(p[i], p[i + 4]);
            return this;
        }

        public Wireframe Build() => new(_lines);
    }

    private static Vec3 V(double x, double y, double z) => new(x, y, z);

    /// <summary>
    /// A TIE fighter: the ball cockpit with its round front window, and the two hexagonal wing panels
    /// on short pylons either side. About two units across.
    /// </summary>
    public static readonly Wireframe Tie = BuildTie(bent: false);

    /// <summary>Vader's TIE: the same cockpit, but bent-winged, with a bulge behind.</summary>
    public static readonly Wireframe VaderTie = BuildTie(bent: true);

    private static Wireframe BuildTie(bool bent)
    {
        var b = new Builder();

        // The cockpit ball: three rings, and the window's frame on the front.
        double r = bent ? 0.36 : 0.32;
        b.Path(true, Builder.FrontRing(V(0, 0, 0), r, 12));
        b.Path(true, Builder.SideRing(V(0, 0, 0), r, 12));
        b.Path(true, Builder.FlatRing(V(0, 0, 0), r, 12));
        var window = Builder.FrontRing(V(0, 0, r * 0.95), r * 0.55, 8, Math.PI / 8);
        b.Path(true, window);
        for (int i = 0; i < 8; i += 2)
            b.Line(window[i], V(0, 0, r));
        if (bent)
            b.Tube(0, 0, -r * 0.6, -r * 1.7, r * 0.55, 8);

        foreach (double side in new[] { -1.0, 1.0 })
        {
            // Pylons out to the wings.
            b.Line(V(side * r * 0.9, 0.07, 0), V(side * 0.92, 0.07, 0));
            b.Line(V(side * r * 0.9, -0.07, 0), V(side * 0.92, -0.07, 0));

            if (!bent)
            {
                // A tall hexagon, with spokes from its hub to each corner.
                var hex = new Vec3[6];
                for (int i = 0; i < 6; i++)
                {
                    double a = Math.PI / 2 + i * Math.PI / 3;
                    hex[i] = V(side * 0.95, Math.Sin(a) * 1.15, Math.Cos(a) * 0.95);
                }
                b.Path(true, hex);
                var hub = Builder.SideRing(V(side * 0.95, 0, 0), 0.16, 6);
                b.Path(true, hub);
                for (int i = 0; i < 6; i++)
                    b.Line(hub[i], hex[i]);
            }
            else
            {
                // Bent wings: a flat middle panel, with the top and bottom folded in toward the cockpit.
                var mid = new[] { V(side * 0.9, 0.35, 0.65), V(side * 0.9, -0.35, 0.65), V(side * 0.9, -0.35, -0.65), V(side * 0.9, 0.35, -0.65) };
                b.Path(true, mid);
                b.Path(false, mid[0], V(side * 0.55, 1.1, 0.42), V(side * 0.55, 1.1, -0.42), mid[3]);
                b.Path(false, mid[1], V(side * 0.55, -1.1, 0.42), V(side * 0.55, -1.1, -0.42), mid[2]);
                b.Line(V(side * 0.9, 0, 0.65), V(side * 0.9, 0, -0.65));
            }
        }

        return b.Build();
    }

    /// <summary>
    /// An X-wing, for the title screen: the long nose, four wings in an X, a laser cannon at each tip
    /// and an engine at each wing root. Nose at +Z.
    /// </summary>
    public static readonly Wireframe XWing = BuildXWing();

    private static Wireframe BuildXWing()
    {
        var b = new Builder();

        // Fuselage: a tapering box from the cockpit to the nose.
        var rear = new[] { V(-0.16, 0.14, -0.9), V(0.16, 0.14, -0.9), V(0.16, -0.12, -0.9), V(-0.16, -0.12, -0.9) };
        var mid = new[] { V(-0.14, 0.2, 0.1), V(0.14, 0.2, 0.1), V(0.14, -0.1, 0.1), V(-0.14, -0.1, 0.1) };
        var nose = new[] { V(-0.05, 0.05, 1.5), V(0.05, 0.05, 1.5), V(0.05, -0.04, 1.5), V(-0.05, -0.04, 1.5) };
        b.Path(true, rear).Path(true, mid).Path(true, nose);
        for (int i = 0; i < 4; i++)
            b.Line(rear[i], mid[i]).Line(mid[i], nose[i]);

        // Cockpit canopy.
        b.Path(false, V(-0.1, 0.2, 0.05), V(-0.07, 0.3, -0.2), V(0.07, 0.3, -0.2), V(0.1, 0.2, 0.05));
        b.Line(V(-0.07, 0.3, -0.2), V(-0.1, 0.2, -0.45)).Line(V(0.07, 0.3, -0.2), V(0.1, 0.2, -0.45));

        // Four wings in the attack position, each with its engine and its cannon.
        foreach (double sx in new[] { -1.0, 1.0 })
        foreach (double sy in new[] { -1.0, 1.0 })
        {
            var rootFront = V(sx * 0.16, sy * 0.06, -0.25);
            var rootBack = V(sx * 0.16, sy * 0.06, -0.85);
            var tipFront = V(sx * 1.25, sy * 0.42, -0.4);
            var tipBack = V(sx * 1.25, sy * 0.42, -0.85);
            b.Path(true, rootFront, tipFront, tipBack, rootBack);
            b.Tube(sx * 0.3, sy * 0.14, -0.35, -1.0, 0.09, 6);
            b.Line(V(sx * 1.25, sy * 0.42, -0.4), V(sx * 1.25, sy * 0.42, 0.75));
            b.Line(V(sx * 1.22, sy * 0.39, -0.1), V(sx * 1.22, sy * 0.39, 0.4));
        }

        return b.Build();
    }

    /// <summary>A laser bunker on the Death Star's surface: a squat dome on a base, with its gun. Radius 1.</summary>
    public static readonly Wireframe Bunker = BuildBunker();

    private static Wireframe BuildBunker()
    {
        var b = new Builder();
        b.Path(true, V(-1, 0, -1), V(1, 0, -1), V(1, 0, 1), V(-1, 0, 1));
        var rim = Builder.FlatRing(V(0, 0.25, 0), 0.75, 10);
        var top = Builder.FlatRing(V(0, 0.65, 0), 0.4, 10);
        b.Path(true, rim).Path(true, top);
        for (int i = 0; i < 10; i += 2)
            b.Line(rim[i], top[i]);
        for (int i = 0; i < 4; i++)
            b.Line(V(i < 2 ? -1 : 1, 0, i % 2 == 0 ? -1 : 1), rim[(i * 5 + 1) % 10]);
        b.Tube(0, 0.6, 0.2, 1.4, 0.09, 4);
        return b.Build();
    }

    /// <summary>A gun turret on the trench wall or floor: a box with twin barrels. Radius 1, barrels along +Z.</summary>
    public static readonly Wireframe Turret = BuildTurret();

    private static Wireframe BuildTurret()
    {
        var b = new Builder();
        b.Box(V(-0.7, -0.5, -0.7), V(0.7, 0.4, 0.7));
        b.Path(true, Builder.FlatRing(V(0, 0.4, 0), 0.45, 8));
        b.Line(V(-0.2, 0.25, 0.7), V(-0.2, 0.25, 1.6)).Line(V(0.2, 0.25, 0.7), V(0.2, 0.25, 1.6));
        return b.Build();
    }
}
