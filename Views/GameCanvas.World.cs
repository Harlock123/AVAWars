using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AVAWars.Engine;
using AVAWars.Models;

namespace AVAWars.Views;

// The world outside the cockpit: stars, the Death Star, its surface and trench, and everything flying.
public partial class GameCanvas
{
    private void DrawWorld(DrawingContext context)
    {
        DrawStars(context);

        switch (_engine.Phase)
        {
            case Phase.Space:
                DrawDeathStar(context, _engine.DeathStarPosition, 1);
                break;
            case Phase.Surface:
                DrawSurface(context);
                break;
            case Phase.Trench:
                DrawTrench(context);
                break;
            case Phase.Escape:
                if (!_engine.DeathStarGone)
                    DrawDeathStar(context, _engine.DeathStarPosition, 1);
                else
                    DrawShockwave(context);
                break;
        }

        DrawThings(context);
        DrawTorpedoes(context);
        DrawLasers(context);

        if (_engine.HitFlash > 0)
        {
            var flash = _engine.Phase == Phase.Escape ? Colors.White : AlertRed;
            context.FillRectangle(GetBrush(flash, Math.Min(0.45, _engine.HitFlash)), new Rect(0, 0, ScreenWidth, ScreenHeight));
        }
    }

    private void DrawStars(DrawingContext context)
    {
        double roll = _engine.Camera.Roll;
        for (int i = 0; i < _engine.Stars.Count; i++)
        {
            var dir = _engine.Stars[i].RotateZ(roll);
            if (dir.Z < 0.1) continue;
            var p = Camera.Project(dir);
            if (p.X < -2 || p.X > ScreenWidth + 2 || p.Y < -2 || p.Y > ScreenHeight + 2) continue;
            double brightness = 0.35 + 0.65 * ((i * 37) % 100) / 100.0;
            double twinkle = 0.75 + 0.25 * Math.Sin(Clock * (0.8 + brightness) + i);
            context.FillRectangle(GetBrush(Color.FromRgb(210, 220, 255), brightness * twinkle), new Rect(p.X - 0.8, p.Y - 0.8, 1.6, 1.6));
        }
    }

    /// <summary>
    /// The Death Star: a wireframe globe with only its near side drawn, the equatorial trench, and the
    /// dish of the superlaser in the northern hemisphere.
    /// </summary>
    private void DrawDeathStar(DrawingContext context, Vec3 centre, double alpha)
    {
        var camera = _engine.Camera;
        double r = GameEngine.DeathStarRadius;
        var color = GameEngine.DeathStarColor;
        var toCamera = (camera.Position - centre).Normalized();

        void Arc(Func<double, Vec3> point, int segments, double width)
        {
            for (int i = 0; i < segments; i++)
            {
                var a = point((double)i / segments);
                var b = point((double)(i + 1) / segments);
                // Only the side facing the ship: a line on the far side is hidden behind the globe.
                var mid = ((a + b) * 0.5 - centre).Normalized();
                double facing = mid.X * toCamera.X + mid.Y * toCamera.Y + mid.Z * toCamera.Z;
                if (facing <= 0) continue;
                DrawWorldLine(context, a, b, color, alpha * (0.35 + 0.65 * facing), width);
            }
        }

        // Lines of latitude, and of longitude.
        for (int lat = -3; lat <= 3; lat++)
        {
            double phi = lat * Math.PI / 8;
            Arc(t => centre + new Vec3(Math.Cos(t * Math.PI * 2) * Math.Cos(phi), Math.Sin(phi), Math.Sin(t * Math.PI * 2) * Math.Cos(phi)) * r,
                36, lat == 0 ? 0.8 : 1.0);
        }
        for (int lon = 0; lon < 12; lon++)
        {
            double theta = lon * Math.PI / 6;
            Arc(t => centre + new Vec3(Math.Cos(theta) * Math.Sin(t * Math.PI), Math.Cos(t * Math.PI), Math.Sin(theta) * Math.Sin(t * Math.PI)) * r,
                24, 1.0);
        }

        // The equatorial trench: two lines just either side of the equator.
        foreach (double offset in new[] { -0.03, 0.03 })
            Arc(t => centre + new Vec3(Math.Cos(t * Math.PI * 2), offset, Math.Sin(t * Math.PI * 2)).Normalized() * r, 48, 1.8);

        // The superlaser dish, up and to one side, facing the ship.
        var dishCentre = new Vec3(-0.42, 0.52, -0.74).Normalized();
        var u = new Vec3(dishCentre.Z, 0, -dishCentre.X).Normalized();
        var v = new Vec3(
            dishCentre.Y * u.Z - dishCentre.Z * u.Y,
            dishCentre.Z * u.X - dishCentre.X * u.Z,
            dishCentre.X * u.Y - dishCentre.Y * u.X);
        foreach (double size in new[] { 0.22, 0.06 })
            Arc(t =>
            {
                double a = t * Math.PI * 2;
                var dir = (dishCentre + u * (Math.Cos(a) * size) + v * (Math.Sin(a) * size)).Normalized();
                return centre + dir * r * (size > 0.1 ? 1 : 0.97);
            }, 24, 1.6);
    }

    /// <summary>The flat, tilted ring of light that races out from the Death Star as it goes up.</summary>
    private void DrawShockwave(DrawingContext context)
    {
        double t = _engine.EscapeClock - 3.0;
        if (t > 3) return;
        var c = _engine.Camera.ToCamera(_engine.DeathStarPosition);
        if (c.Z < Camera.Near) return;
        var p = Camera.Project(c);
        double alpha = 1 - t / 3;
        foreach (var (grow, color) in new[] { (1.0, Colors.White), (0.8, GameEngine.FireballColor) })
        {
            double r = (60 + t * 520) * grow;
            var points = new Point[48];
            for (int i = 0; i < points.Length; i++)
            {
                double a = i * Math.PI * 2 / points.Length;
                // Tipped well over, like a ring seen nearly edge-on.
                points[i] = new Point(p.X + Math.Cos(a) * r, p.Y + Math.Sin(a) * r * 0.22);
            }
            DrawGlowPolygon(context, points, color, alpha, 2.5);
        }
    }

    // ----- The surface -----

    /// <summary>Fills the ground below the horizon so the stars don't show through it.</summary>
    private void FillBelowHorizon(DrawingContext context, double groundY, Color color)
    {
        var camera = _engine.Camera;
        var left = camera.ToCamera(new Vec3(camera.Position.X - 200000, groundY, 100000));
        var right = camera.ToCamera(new Vec3(camera.Position.X + 200000, groundY, 100000));
        var pl = Camera.Project(left);
        var pr = Camera.Project(right);
        bool groundBelow = camera.Position.Y > groundY;
        double edge = groundBelow ? ScreenHeight + 2000 : -2000;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(pl, true);
            g.LineTo(pr);
            g.LineTo(new Point(pr.X, edge));
            g.LineTo(new Point(pl.X, edge));
            g.EndFigure(true);
        }
        context.DrawGeometry(GetBrush(color, 1), null, geometry);
    }

    private void DrawSurface(DrawingContext context)
    {
        FillBelowHorizon(context, 0, Color.FromRgb(2, 4, 14));
        var color = GameEngine.GroundColor;
        double camX = _engine.Camera.Position.X;

        // A grid ruled across the surface, sliding toward the ship.
        double spacingX = 90, spacingZ = 160, far = 3200;
        double startX = Math.Floor((camX - 1600) / spacingX) * spacingX;
        for (double x = startX; x <= camX + 1600; x += spacingX)
            DrawWorldLine(context, new Vec3(x, 0, 6), new Vec3(x, 0, far), color, 0.45, 1);
        double offset = _engine.Travelled % spacingZ;
        for (double z = spacingZ - offset; z < far; z += spacingZ)
            DrawWorldLine(context, new Vec3(camX - 1700, 0, z), new Vec3(camX + 1700, 0, z), color, 0.55 * DistanceFade(z, 1600, far), 1);

        // Towers: a tall block, with a gun on top that can be shot off.
        foreach (var tower in _engine.Towers.OrderByDescending(t => t.Position.Z))
            DrawTower(context, tower);
    }

    private void DrawTower(DrawingContext context, Tower tower)
    {
        var p = tower.Position;
        double hw = tower.HalfWidth, h = tower.Height;
        double alpha = DistanceFade(_engine.Camera.ToCamera(p).Z);
        DrawBox(context, p + new Vec3(-hw, 0, -hw), p + new Vec3(hw, h, hw), GameEngine.TowerColor, alpha);
        if (tower.TopAlive)
        {
            double cap = hw * 0.65;
            DrawBox(context, p + new Vec3(-cap, h, -cap), p + new Vec3(cap, h + 16, cap), GameEngine.TurretColor, alpha);
            DrawWorldLine(context, p + new Vec3(0, h + 12, 0), p + new Vec3(0, h + 12, -26), GameEngine.TurretColor, alpha, 1.5);
        }
    }

    private void DrawBox(DrawingContext context, Vec3 min, Vec3 max, Color color, double alpha)
    {
        var c = new[]
        {
            new Vec3(min.X, min.Y, min.Z), new Vec3(max.X, min.Y, min.Z), new Vec3(max.X, min.Y, max.Z), new Vec3(min.X, min.Y, max.Z),
            new Vec3(min.X, max.Y, min.Z), new Vec3(max.X, max.Y, min.Z), new Vec3(max.X, max.Y, max.Z), new Vec3(min.X, max.Y, max.Z)
        };
        double width = Math.Clamp(420 / Math.Max(_engine.Camera.ToCamera(min).Z, 1), 1.0, 2.4);
        for (int i = 0; i < 4; i++)
        {
            DrawWorldLine(context, c[i], c[(i + 1) % 4], color, alpha, width);
            DrawWorldLine(context, c[i + 4], c[(i + 1) % 4 + 4], color, alpha, width);
            DrawWorldLine(context, c[i], c[i + 4], color, alpha, width);
        }
    }

    // ----- The trench -----

    /// <summary>Fills one flat face of the trench (between four world points) so stars don't show through.</summary>
    private void FillQuad(DrawingContext context, Vec3 a, Vec3 b, Vec3 c, Vec3 d, Color color)
    {
        var camera = _engine.Camera;
        var points = new[] { a, b, c, d }.Select(camera.ToCamera).Select(p => p.Z < Camera.Near ? p with { Z = Camera.Near } : p).Select(Camera.Project).ToArray();
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], true);
            for (int i = 1; i < 4; i++) g.LineTo(points[i]);
            g.EndFigure(true);
        }
        context.DrawGeometry(GetBrush(color, 1), null, geometry);
    }

    private void DrawTrench(DrawingContext context)
    {
        double w = GameEngine.TrenchHalfWidth, d = GameEngine.TrenchDepth, far = 3600, near = 5;
        var color = GameEngine.TrenchColor;
        var dark = Color.FromRgb(2, 5, 16);

        // The surface either side of the trench, then the walls and floor, all solid.
        FillQuad(context, new Vec3(-4000, 0, near), new Vec3(-w, 0, near), new Vec3(-w, 0, far), new Vec3(-4000, 0, far), dark);
        FillQuad(context, new Vec3(w, 0, near), new Vec3(4000, 0, near), new Vec3(4000, 0, far), new Vec3(w, 0, far), dark);
        FillQuad(context, new Vec3(-w, 0, near), new Vec3(-w, -d, near), new Vec3(-w, -d, far), new Vec3(-w, 0, far), dark);
        FillQuad(context, new Vec3(w, 0, near), new Vec3(w, -d, near), new Vec3(w, -d, far), new Vec3(w, 0, far), dark);
        FillQuad(context, new Vec3(-w, -d, near), new Vec3(w, -d, near), new Vec3(w, -d, far), new Vec3(-w, -d, far), dark);

        // Long lines down the trench: the rims, the floor's edges and a rail along each wall.
        foreach (double side in new[] { -1.0, 1.0 })
        {
            DrawWorldLine(context, new Vec3(side * w, 0, near), new Vec3(side * w, 0, far), color, 0.9, 1.6);
            DrawWorldLine(context, new Vec3(side * w, -d, near), new Vec3(side * w, -d, far), color, 0.9, 1.6);
            DrawWorldLine(context, new Vec3(side * w, -d * 0.35, near), new Vec3(side * w, -d * 0.35, far), color, 0.35, 1);
            DrawWorldLine(context, new Vec3(side * w, -d * 0.7, near), new Vec3(side * w, -d * 0.7, far), color, 0.35, 1);
            DrawWorldLine(context, new Vec3(side * (w + 140), 0, near), new Vec3(side * (w + 140), 0, far), color, 0.3, 1);
        }

        // Ribs across the walls and floor, sliding toward the ship.
        double spacing = 140, offset = _engine.Travelled % spacing;
        for (double z = spacing - offset; z < far; z += spacing)
        {
            double fade = DistanceFade(z, 2000, far);
            DrawWorldLine(context, new Vec3(-w, 0, z), new Vec3(-w, -d, z), color, 0.6 * fade, 1.2);
            DrawWorldLine(context, new Vec3(w, 0, z), new Vec3(w, -d, z), color, 0.6 * fade, 1.2);
            DrawWorldLine(context, new Vec3(-w, -d, z), new Vec3(w, -d, z), color, 0.5 * fade, 1.2);
            DrawWorldLine(context, new Vec3(-w - 140, 0, z), new Vec3(-w, 0, z), color, 0.25 * fade, 1);
            DrawWorldLine(context, new Vec3(w, 0, z), new Vec3(w + 140, 0, z), color, 0.25 * fade, 1);
        }

        foreach (var c in _engine.Catwalks)
        {
            double fade = DistanceFade(c.Position.Z, 2000, far);
            var y = c.Position.Y;
            DrawBox(context, new Vec3(-w, y - Catwalk.HalfThickness, c.Position.Z - 8), new Vec3(w, y + Catwalk.HalfThickness, c.Position.Z + 8),
                Color.FromRgb(255, 150, 60), fade);
        }

        DrawExhaustPort(context);
    }

    private void DrawExhaustPort(DrawingContext context)
    {
        double distance = _engine.PortDistance;
        if (distance > 3400 || distance < -20) return;
        var p = _engine.PortPosition;
        double s = GameEngine.PortHalfSize;
        var color = _engine.IsPortInRange && Blink(6) ? Colors.White : GameEngine.TurretColor;

        Vec3 At(double x, double z) => p + new Vec3(x, 0.5, z);
        var outer = new[] { At(-s * 2, -s * 2), At(s * 2, -s * 2), At(s * 2, s * 2), At(-s * 2, s * 2) };
        var inner = new[] { At(-s, -s), At(s, -s), At(s, s), At(-s, s) };
        for (int i = 0; i < 4; i++)
        {
            DrawWorldLine(context, outer[i], outer[(i + 1) % 4], color, 1, 1.8);
            DrawWorldLine(context, inner[i], inner[(i + 1) % 4], color, 1, 2.2);
            DrawWorldLine(context, outer[i], inner[i], color, 0.6, 1.2);
        }
    }

    // ----- Everything that flies, far to near -----

    private void DrawThings(DrawingContext context)
    {
        var camera = _engine.Camera;
        var list = new List<(double Depth, Action Draw)>();
        void Add(Vec3 at, Action draw) => list.Add((camera.ToCamera(at).Z, draw));

        foreach (var b in _engine.Bunkers)
            Add(b.Position, () => DrawModel(context, Wireframe.Bunker, b.Position, 20, Math.PI, 0, GameEngine.TurretColor, 1));
        foreach (var t in _engine.Turrets)
            Add(t.Position, () => DrawModel(context, Wireframe.Turret, t.Position, 13, Math.PI, t.Facing * Math.PI / 2, GameEngine.TurretColor, 1));
        foreach (var tie in _engine.Ties)
        {
            var color = tie.HitFlash > 0 ? Colors.White : GameEngine.TieColor;
            Add(tie.Position, () => DrawModel(context, Wireframe.Tie, tie.Position, 24, Math.PI + tie.Yaw, tie.Bank, color, 1));
        }
        if (_engine.Vader is { } v)
        {
            var color = v.HitFlash > 0 ? Colors.White : GameEngine.VaderColor;
            double roll = v.IsKnockedOut ? v.SpinOut * 9 : Math.Sin(v.Weave * 1.3) * 0.3;
            Add(v.Position, () => DrawModel(context, Wireframe.VaderTie, v.Position, 28, Math.PI, roll, color, 1));
        }
        foreach (var f in _engine.Fireballs)
            Add(f.Position, () => DrawFireball(context, f));
        foreach (var e in _engine.Explosions)
            Add(e.Position, () => DrawExplosion(context, e));

        foreach (var (_, draw) in list.OrderByDescending(t => t.Depth))
            draw();
    }

    /// <summary>A fireball: a spinning, spiky star of flame with a white-hot middle.</summary>
    private void DrawFireball(DrawingContext context, Fireball f)
    {
        var c = _engine.Camera.ToCamera(f.Position);
        if (c.Z < Camera.Near) return;
        var p = Camera.Project(c);
        double r = Math.Clamp(Camera.ProjectedSize(f.Radius, c.Z), 2, 120);
        double alpha = DistanceFade(c.Z);
        context.DrawEllipse(GetBrush(GameEngine.FireballColor, 0.3 * alpha), null, p, r, r);
        for (int i = 0; i < 8; i++)
        {
            double a = f.Spin + i * Math.PI / 4;
            double len = r * (i % 2 == 0 ? 1.5 : 1.0);
            DrawGlowLine(context, p, p + new Vector(Math.Cos(a), Math.Sin(a)) * len, GameEngine.FireballColor, alpha, Math.Clamp(r / 8, 1, 3));
        }
        context.DrawEllipse(GetBrush(Color.FromRgb(255, 240, 200), alpha), null, p, r * 0.35, r * 0.35);
    }

    private void DrawExplosion(DrawingContext context, Explosion e)
    {
        double alpha = e.Opacity;
        foreach (var (a, b) in e.GetLines())
            DrawWorldLine(context, a, b, e.Color, alpha, 1.8);
    }

    private void DrawTorpedoes(DrawingContext context)
    {
        foreach (var t in _engine.Torpedoes)
        {
            var c = _engine.Camera.ToCamera(t.Position);
            if (c.Z < Camera.Near) continue;
            var p = Camera.Project(c);
            double r = Math.Clamp(Camera.ProjectedSize(6, c.Z), 2, 14);
            context.DrawEllipse(GetBrush(Color.FromRgb(255, 200, 120), 0.35), null, p, r * 2.4, r * 2.4);
            context.DrawEllipse(GetBrush(Colors.White, 1), null, p, r, r);
        }
    }

    // The four cannons, one in each corner of the view, fire together at the crosshair.
    private void DrawLasers(DrawingContext context)
    {
        var guns = new[] { new Point(40, ScreenHeight - 20), new Point(ScreenWidth - 40, ScreenHeight - 20), new Point(40, 110), new Point(ScreenWidth - 40, 110) };
        foreach (var shot in _engine.Lasers)
        {
            double t = shot.Age / LaserShot.ShowTime;
            foreach (var gun in guns)
            {
                // A short bolt racing from the gun toward the aim point.
                var from = gun + (shot.Target - gun) * Math.Min(1, t * 1.1);
                var to = gun + (shot.Target - gun) * Math.Min(1, t * 1.1 + 0.25);
                DrawGlowLine(context, from, to, LaserColor, 1 - t * 0.5, 3);
            }
            if (shot.Hit && t > 0.6)
                context.DrawEllipse(GetBrush(Colors.White, 0.8), null, shot.Target, 7, 7);
        }
    }
}
