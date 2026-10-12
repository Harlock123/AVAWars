using System;
using Avalonia;
using Avalonia.Media;
using AVAWars.Engine;
using AVAWars.Models;

namespace AVAWars.Views;

// The cockpit readouts, and the title, entry and high-score screens.
public partial class GameCanvas
{
    // ----- In flight -----

    private void DrawHud(DrawingContext context)
    {
        if (_engine.State == GameState.EnterInitials) return;

        context.DrawText(Text("SCORE", 13, TextColor, 0.8), new Point(24, 14));
        context.DrawText(Text(_engine.Score.ToString(), 28, Colors.White), new Point(24, 28));

        int high = _engine.State == GameState.Attract ? _engine.HighScore : Math.Max(_engine.HighScore, _engine.Score);
        DrawRightText(context, "WAVE", 13, ScreenWidth - 24, 14, TextColor);
        DrawRightText(context, _engine.Wave.ToString(), 28, ScreenWidth - 24, 28, Gold);
        DrawCentredText(context, $"HIGH {high}", 13, 14, TextColor);

        DrawShields(context);
        DrawPhaseReadout(context);

        if (_engine.IsInPlay && _engine.Phase != Phase.Escape)
            DrawCrosshair(context);

        if (_engine.BannerTime > 0 && _engine.Banner != null && _engine.State != GameState.GameOver)
        {
            double alpha = Math.Min(1, _engine.BannerTime * 2);
            DrawCentredText(context, _engine.Banner, 34, 190, Gold, alpha: alpha);
            if (_engine.SubBanner != null)
                DrawCentredText(context, _engine.SubBanner, 18, 238, TextColor, alpha: alpha);
        }

        if (_engine.Phase == Phase.Escape && _engine.DeathStarGone)
        {
            double y = 560;
            foreach (var line in _engine.Tally)
            {
                DrawCentredText(context, line, 20, y, Colors.White);
                y += 32;
            }
        }

        var flags = (_engine.Sound.IsMuted ? "MUTED  " : "") + (!_engine.Sound.IsVoiceOn ? "VOICE OFF" : "");
        if (flags.Length > 0)
            DrawRightText(context, flags.Trim(), 11, ScreenWidth - 24, 66, TextColor);
    }

    // Shields: a row of blocks under the high score, red when nearly gone.
    private void DrawShields(DrawingContext context)
    {
        int shields = _engine.Shields;
        var color = shields > 2 ? Color.FromRgb(90, 255, 140) : AlertRed;
        bool flash = shields <= 2 && _engine.IsInPlay && Blink(3);
        DrawCentredText(context, "SHIELDS", 12, 34, TextColor);
        double w = 26, gap = 6, total = GameEngine.MaxShields * w + (GameEngine.MaxShields - 1) * gap;
        double x = (ScreenWidth - total) / 2;
        for (int i = 0; i < GameEngine.MaxShields; i++)
        {
            var r = new Rect(x + i * (w + gap), 52, w, 12);
            if (i < shields)
                context.FillRectangle(GetBrush(color, flash ? 0.4 : 0.9), r);
            else
                context.DrawRectangle(GetPen(color, 0.3, 1), r);
        }
    }

    private void DrawPhaseReadout(DrawingContext context)
    {
        if (!_engine.IsInPlay) return;
        string text = _engine.Phase switch
        {
            Phase.Space when _engine.ApproachTimer <= 0 => $"TIE FIGHTERS  {Math.Min(_engine.TiesDestroyed, _engine.TiesNeeded)} / {_engine.TiesNeeded}",
            Phase.Surface => _engine.TowersTotal > 0
                ? $"TOWER TOPS  {_engine.TowerTopsDestroyed} / {_engine.TowersTotal}     TRENCH IN {_engine.SurfaceLeft / 100:0}"
                : $"TRENCH IN {_engine.SurfaceLeft / 100:0}",
            Phase.Trench => $"EXHAUST PORT  {Math.Max(0, _engine.PortDistance) / 100:0}",
            _ => ""
        };
        if (text.Length > 0)
            DrawCentredText(context, text, 15, ScreenHeight - 34, TextColor);

        if (_engine.Phase == Phase.Trench && _engine.Vader is { IsKnockedOut: false } v)
            DrawCentredText(context, $"VADER  {new string('|', v.HitsLeft)}", 14, ScreenHeight - 58, GameEngine.VaderColor);

        if (_engine.IsPortInRange)
        {
            // A targeting box round the port, and the call to fire.
            var c = _engine.Camera.ToCamera(_engine.PortPosition);
            if (c.Z > Camera.Near)
            {
                var p = Camera.Project(c);
                double s = Camera.ProjectedSize(GameEngine.PortHalfSize, c.Z) + 22;
                var box = new Rect(p.X - s, p.Y - s * 0.6, s * 2, s * 1.2);
                DrawFrame(context, box, Blink(8) ? Colors.White : Gold, 0.9);
            }
            if (Blink(4))
                DrawCentredText(context, "FIRE TORPEDO", 26, ScreenHeight - 96, Gold);
        }
    }

    // The aiming cross: four short arms round a gap, brighter when it is over something.
    private void DrawCrosshair(DrawingContext context)
    {
        var c = _engine.CrosshairScreen;
        bool onTarget = _engine.TargetAt(c) != null;
        var color = onTarget ? Colors.White : CrosshairColor;
        double gap = 8, arm = onTarget ? 26 : 20;
        DrawGlowLine(context, c + new Vector(-gap - arm, 0), c + new Vector(-gap, 0), color, 0.9, 2);
        DrawGlowLine(context, c + new Vector(gap, 0), c + new Vector(gap + arm, 0), color, 0.9, 2);
        DrawGlowLine(context, c + new Vector(0, -gap - arm), c + new Vector(0, -gap), color, 0.9, 2);
        DrawGlowLine(context, c + new Vector(0, gap), c + new Vector(0, gap + arm), color, 0.9, 2);
    }

    // ----- Title screen -----

    private void DrawTitleScreen(DrawingContext context, bool withText)
    {
        DrawStars(context);

        // The X-wing turning slowly over the stars.
        var centre = new Point(ScreenWidth / 2, 268);
        DrawModelShowcase(context, Wireframe.XWing, centre, 150, Clock * 0.45 + 2.6, 0.3, Color.FromRgb(225, 235, 255), 1, 1.6);

        if (!withText) return;

        DrawCentredText(context, "AVAWARS", 76, 12, Gold);
        DrawCentredText(context, "THE ATTACK ON THE DEATH STAR", 20, 100, TextColor);

        var legend = new (Wireframe? Model, Color Color, string Name, string Value, double Size)[]
        {
            (Wireframe.Tie, GameEngine.TieColor, "TIE FIGHTER", TieFighter.Points.ToString(), 30),
            (Wireframe.VaderTie, GameEngine.VaderColor, "VADER (PER HIT)", VaderTie.PointsPerHit.ToString(), 30),
            (null, GameEngine.FireballColor, "FIREBALL", Fireball.Points.ToString(), 0),
            (Wireframe.Bunker, GameEngine.TurretColor, "BUNKER / TURRET", $"{Bunker.Points} / {Turret.Points}", 22),
            (null, GameEngine.TowerColor, "TOWER TOP", "200 +200", 0)
        };
        for (int i = 0; i < legend.Length; i++)
        {
            var (model, color, name, value, size) = legend[i];
            double cx = 112 + i * 200;
            var at = new Point(cx, 430);
            if (model != null)
                DrawModelShowcase(context, model, at, size, Clock * 0.8 + i, 0.35, color, 1, 1.3);
            else if (name == "FIREBALL")
                DrawTitleFireball(context, at);
            else
                DrawTitleTower(context, at);
            DrawCentredText(context, name, 13, 470, color, cx);
            DrawCentredText(context, value, 15, 488, Colors.White, cx);
        }

        var controls = new (string Keys, string Action)[]
        {
            ("Mouse", "Aim and steer; click to fire"),
            ("Arrows / WASD", "Steer the crosshair"),
            ("SPACE or CTRL", "Fire lasers / torpedo"),
            ("Gamepad", "Stick steers, any button fires"),
            ("ESC  M  V", "Pause, mute, voice")
        };
        string[] orders =
        {
            "Fight through the TIEs, skim the surface,",
            "then run the trench and put a torpedo",
            "down the exhaust port. Shields don't",
            "come back: one returns per Death Star.",
            "Fire nothing in the trench but the torpedo",
            "for the Use the Force bonus."
        };
        for (int i = 0; i < orders.Length; i++)
        {
            double y = 522 + i * 20;
            if (i < controls.Length)
            {
                context.DrawText(Text(controls[i].Keys, 14, Colors.White), new Point(48, y));
                context.DrawText(Text(controls[i].Action, 14, TextColor), new Point(200, y));
            }
            context.DrawText(Text(orders[i], 14, TextColor), new Point(560, y));
        }

        var best = _engine.Table.Entries.Count > 0 ? _engine.Table.Entries[0] : null;
        DrawCentredText(context, best == null ? "HIGH SCORE 0" : $"HIGH SCORE  {best.Initials.TrimEnd()}  {best.Score}", 18, 680, Gold);
        if (Blink(2))
            DrawCentredText(context, "Press SPACE or click to start", 26, 712, Colors.White);
    }

    private void DrawTitleFireball(DrawingContext context, Point p)
    {
        double spin = Clock * 6;
        context.DrawEllipse(GetBrush(GameEngine.FireballColor, 0.3), null, p, 14, 14);
        for (int i = 0; i < 8; i++)
        {
            double a = spin + i * Math.PI / 4;
            DrawGlowLine(context, p, p + new Vector(Math.Cos(a), Math.Sin(a)) * (i % 2 == 0 ? 22 : 15), GameEngine.FireballColor, 1, 2);
        }
        context.DrawEllipse(GetBrush(Color.FromRgb(255, 240, 200), 1), null, p, 5, 5);
    }

    private void DrawTitleTower(DrawingContext context, Point p)
    {
        var body = new Rect(p.X - 12, p.Y - 26, 24, 44);
        DrawFrame(context, body, GameEngine.TowerColor, 1);
        DrawFrame(context, new Rect(p.X - 8, p.Y - 36, 16, 10), GameEngine.TurretColor, 1);
    }

    // ----- High scores -----

    private string NewEntryHeadline => _engine.Score > _engine.HighScore ? "NEW HIGH SCORE" : "YOU MADE THE TOP TEN";

    private void DrawInitialsEntry(DrawingContext context)
    {
        var entry = _engine.Entry;
        if (entry == null) return;

        DrawPanel(context, new Rect(152, 110, 720, 560));
        DrawCentredText(context, NewEntryHeadline, 40, 140, Gold);
        DrawCentredText(context, $"SCORE {_engine.Score}    WAVE {_engine.Wave}", 22, 200, TextColor);
        DrawCentredText(context, "ENTER YOUR INITIALS, PILOT", 24, 260, Colors.White);

        bool blinkOn = Blink(3);
        const double cell = 100;
        double left = (ScreenWidth - cell * HighScoreTable.InitialsLength) / 2;
        string initials = entry.Initials;

        for (int i = 0; i < initials.Length; i++)
        {
            bool current = i == entry.Cursor;
            char c = initials[i];
            var letter = Text(c == ' ' ? "_" : c.ToString(), 76, current ? Colors.White : Gold);
            double x = left + i * cell + (cell - letter.Width) / 2;
            context.DrawText(letter, new Point(x, 315));

            if (current)
            {
                var up = Text("^", 22, TextColor);
                var down = Text("v", 22, TextColor);
                context.DrawText(up, new Point(left + i * cell + (cell - up.Width) / 2, 300));
                context.DrawText(down, new Point(left + i * cell + (cell - down.Width) / 2, 418));
                if (blinkOn)
                    DrawGlowLine(context, new Point(left + i * cell + 18, 412), new Point(left + (i + 1) * cell - 18, 412), Colors.White, 1, 2.5);
            }
        }

        string[] help =
        {
            "Type your initials, or UP/DOWN to choose a letter",
            "and LEFT/RIGHT to move.  ENTER when done.",
            "Gamepad: D-pad to choose, X to accept, START when done",
            "Mouse: wheel to choose, click to accept"
        };
        double y = 480;
        foreach (var line in help)
        {
            DrawCentredText(context, line, 16, y, TextColor);
            y += 26;
        }
    }

    private void DrawHighScoreTable(DrawingContext context)
    {
        DrawPanel(context, new Rect(192, 60, 640, 640));
        DrawCentredText(context, "HIGH SCORES", 44, 80, Gold);

        const double rankX = 250, nameX = 340, scoreRight = 640, waveX = 690;
        double y = 165;
        context.DrawText(Text("RANK", 16, TextColor), new Point(rankX, y));
        context.DrawText(Text("NAME", 16, TextColor), new Point(nameX, y));
        DrawRightText(context, "SCORE", 16, scoreRight, y, TextColor);
        context.DrawText(Text("WAVE", 16, TextColor), new Point(waveX, y));

        bool blinkOn = Blink(3);
        var entries = _engine.Table.Entries;
        y = 200;
        for (int i = 0; i < HighScoreTable.Size; i++)
        {
            bool highlight = i == _engine.HighlightIndex;
            var color = highlight ? (blinkOn ? Colors.White : Gold) : i == 0 ? Gold : Colors.White;

            string name = i < entries.Count ? entries[i].Initials : "---";
            string score = i < entries.Count ? entries[i].Score.ToString() : "0";
            string wave = i < entries.Count && entries[i].Wave > 0 ? entries[i].Wave.ToString() : "-";

            context.DrawText(Text($"{i + 1}.", 24, color), new Point(rankX, y));
            context.DrawText(Text(name, 24, color), new Point(nameX, y));
            DrawRightText(context, score, 24, scoreRight, y, color);
            context.DrawText(Text(wave, 24, color), new Point(waveX + 16, y));

            if (highlight)
                DrawGlowLine(context, new Point(rankX - 20, y + 15), new Point(rankX - 8, y + 15), color, 1, 2);
            y += 42;
        }

        if (Blink(2))
            DrawCentredText(context, "Press SPACE or click to play", 24, 650, Colors.White);
    }
}
