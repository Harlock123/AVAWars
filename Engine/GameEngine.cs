using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using AVAWars.Models;

namespace AVAWars.Engine;

public enum GameState
{
    StartScreen,
    Playing,
    Paused,
    GameOver,
    Attract,
    EnterInitials,
    HighScores
}

/// <summary>The three parts of an attack on the Death Star, and the getaway after.</summary>
public enum Phase
{
    Space,
    Surface,
    Trench,
    Escape
}

/// <summary>
/// The whole game: the attack's phases (in GameEngine.Space, .Surface and .Trench), the X-wing's
/// guns and shields, scoring, and the title, attract and high-score cycle around it.
/// </summary>
public partial class GameEngine : IDisposable
{
    private static readonly Random Random = new();

    public const int StartingShields = 6;
    public const int MaxShields = 6;
    private const double FireInterval = 0.2;
    private const double HitGrace = 0.5;          // seconds after a hit before the next can land
    private const double CrosshairSpeed = 1.7;    // stick travel per second, full deflection
    private const double LaserRange = 1500;

    private const double GameOverInputDelay = 1.0;
    private const double AttractIdleDelay = 20.0;   // seconds on the title screen before the demo
    private const double AttractLength = 80.0;      // longest a demo runs
    private const double GameOverIdleDelay = 20.0;  // seconds on game over before the title returns
    private const double GameOverBeforeEntry = 2.5; // "GAME OVER" shows this long before initials entry
    private const double EntryInputGrace = 0.8;     // ignore input this long, so a mashed fire button doesn't enter letters
    private const double EntryTimeout = 45.0;       // idle this long in entry and the initials are taken as they are
    private const double HighScoresShowTime = 10.0; // the table stays up this long

    public static readonly Color TieColor = Color.FromRgb(120, 255, 140);
    public static readonly Color VaderColor = Color.FromRgb(230, 140, 255);
    public static readonly Color FireballColor = Color.FromRgb(255, 120, 50);
    public static readonly Color GroundColor = Color.FromRgb(60, 140, 255);
    public static readonly Color TowerColor = Color.FromRgb(255, 80, 80);
    public static readonly Color TurretColor = Color.FromRgb(255, 220, 70);
    public static readonly Color TrenchColor = Color.FromRgb(70, 160, 255);
    public static readonly Color DeathStarColor = Color.FromRgb(150, 170, 210);

    public Camera Camera { get; } = new();
    public Phase Phase { get; private set; }
    public GameState State { get; private set; }
    public int Score { get; private set; }
    public int Shields { get; private set; }
    public int Wave { get; private set; } = 1;

    public List<TieFighter> Ties { get; } = new();
    public VaderTie? Vader { get; private set; }
    public List<Fireball> Fireballs { get; } = new();
    public List<Bunker> Bunkers { get; } = new();
    public List<Tower> Towers { get; } = new();
    public List<Turret> Turrets { get; } = new();
    public List<Catwalk> Catwalks { get; } = new();
    public List<Torpedo> Torpedoes { get; } = new();
    public List<LaserShot> Lasers { get; } = new();
    public List<Explosion> Explosions { get; } = new();

    /// <summary>The crosshair, -1 to 1 across and up the screen. It also steers the ship.</summary>
    public double CrosshairX { get; private set; }
    public double CrosshairY { get; private set; }
    public Point CrosshairScreen => Camera.CrosshairToScreen(CrosshairX, CrosshairY);

    /// <summary>How fast the world streams past, in units per second (zero in space).</summary>
    public double Speed { get; private set; }

    // What the screen shows besides the objects themselves.
    public string? Banner { get; private set; }
    public string? SubBanner { get; private set; }
    public double BannerTime { get; private set; }
    public double HitFlash { get; private set; }
    public double Shake { get; private set; }
    public double Clock { get; private set; }
    public bool IsInPlay => State is GameState.Playing or GameState.Attract;

    public HighScoreTable Table { get; } = HighScoreTable.Load();
    public int HighScore => Table.TopScore;
    public InitialsEntry? Entry { get; private set; }
    public int HighlightIndex { get; private set; } = -1;
    public bool IsEntryPending => _entryPending;

    public InputHandler Input { get; } = new();
    public SoundManager Sound { get; } = new();

    private double _fireTimer;
    private bool _wasFiring;
    private double _hitGrace;
    private bool _shieldsLowWarned;

    private bool _wasStartPressed;
    private double _gameOverTimer;
    private bool _entryPending;
    private double _entryClock;
    private double _entryIdle;
    private double _padRepeat;
    private bool _wasPadUp, _wasPadDown, _wasPadLeft, _wasPadRight, _wasPadAccept, _wasPadDone, _wasLeftMouse;
    private double _tableTimer;
    private bool _tableLeadsToDemo;
    private double _idleTimer;
    private double _attractTimer;
    private bool _wasAnyInputLastFrame;
    private readonly AttractPilot _pilot = new();

    public GameEngine()
    {
        State = GameState.StartScreen;
        InitStars();
    }

    public void Dispose()
    {
        // Closing the window mid-entry still keeps the score, under whatever initials are showing.
        if (_entryPending)
            CommitEntry();

        Sound.Dispose();
        Input.Dispose();
    }

    // ----- Starting, stopping and the attract cycle -----

    public void StartNewGame()
    {
        BeginRun(GameState.Playing);
        Sound.PlayFanfare();
        Sound.Say(Voice.Welcome);
    }

    /// <summary>The game plays itself, silently, until the shields go or time runs out.</summary>
    public void StartAttract()
    {
        BeginRun(GameState.Attract);
        _attractTimer = AttractLength;
    }

    public void ReturnToTitle()
    {
        State = GameState.StartScreen;
        Sound.StopAllLoops();
        Sound.ClearVoice();
        Sound.IsSilenced = false;
        ClearAll();
        _idleTimer = 0;
        HighlightIndex = -1;
        Banner = SubBanner = null;
    }

    private void BeginRun(GameState state)
    {
        State = state;
        Sound.StopAllLoops();
        Sound.ClearVoice();
        Sound.IsSilenced = state == GameState.Attract;
        Score = 0;
        Shields = StartingShields;
        Wave = 1;
        CrosshairX = CrosshairY = 0;
        _shieldsLowWarned = false;
        _hitGrace = 0;
        HitFlash = Shake = 0;
        _pilot.Reset();
        StartSpace();
    }

    private void ClearAll()
    {
        Ties.Clear();
        Vader = null;
        Fireballs.Clear();
        Bunkers.Clear();
        Towers.Clear();
        Turrets.Clear();
        Catwalks.Clear();
        Torpedoes.Clear();
        Lasers.Clear();
        Explosions.Clear();
        Camera.Position = Vec3.Zero;
        Camera.Roll = 0;
        Speed = 0;
    }

    private void ShowHighScores(bool leadsToDemo)
    {
        State = GameState.HighScores;
        _tableTimer = HighScoresShowTime;
        _tableLeadsToDemo = leadsToDemo;
        if (leadsToDemo)
            HighlightIndex = -1;
    }

    private void BeginInitials()
    {
        State = GameState.EnterInitials;
        Entry = new InitialsEntry(Table.LastInitials);
        _entryClock = 0;
        _entryIdle = 0;
        _wasPadUp = _wasPadDown = _wasPadLeft = _wasPadRight = _wasPadAccept = _wasPadDone = true;
        _wasLeftMouse = true;
    }

    private void CommitEntry()
    {
        _entryPending = false;
        string initials = Entry?.Initials ?? Table.LastInitials;
        HighlightIndex = Table.Insert(initials, Score, Wave);
        Table.Save();
        Entry = null;
    }

    private bool EntryAcceptsInput => State == GameState.EnterInitials && _entryClock >= EntryInputGrace && Entry != null;

    /// <summary>Keyboard input during initials entry, passed straight from the window.</summary>
    public void InitialsKey(Avalonia.Input.Key key)
    {
        if (!EntryAcceptsInput) return;
        var entry = Entry!;
        _entryIdle = 0;

        if (key >= Avalonia.Input.Key.A && key <= Avalonia.Input.Key.Z)
            entry.Type((char)('A' + (key - Avalonia.Input.Key.A)));
        else if (key >= Avalonia.Input.Key.D0 && key <= Avalonia.Input.Key.D9)
            entry.Type((char)('0' + (key - Avalonia.Input.Key.D0)));
        else if (key >= Avalonia.Input.Key.NumPad0 && key <= Avalonia.Input.Key.NumPad9)
            entry.Type((char)('0' + (key - Avalonia.Input.Key.NumPad0)));
        else if (key is Avalonia.Input.Key.OemPeriod or Avalonia.Input.Key.Decimal)
            entry.Type('.');
        else if (key == Avalonia.Input.Key.Up)
            entry.Cycle(1);
        else if (key == Avalonia.Input.Key.Down)
            entry.Cycle(-1);
        else if (key == Avalonia.Input.Key.Left || key == Avalonia.Input.Key.Back)
            entry.Back();
        else if (key == Avalonia.Input.Key.Right)
            entry.Move(1);
        else if (key == Avalonia.Input.Key.Space)
            entry.Advance();
        else if (key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Escape)
            entry.Finish();
    }

    // Gamepad and mouse input during initials entry: up/down dial the letter (holding repeats),
    // left/right move, a fire button accepts a letter, Start finishes. The wheel dials, a click accepts.
    private void UpdateInitials(double deltaTime)
    {
        _entryClock += deltaTime;
        _entryIdle += deltaTime;
        var entry = Entry!;

        bool up = Input.PadUp, down = Input.PadDown, left = Input.PadLeft, right = Input.PadRight;
        bool accept = Input.PadAccept, done = Input.PadDone, click = Input.IsLeftMousePressed;

        if (EntryAcceptsInput)
        {
            if (up || down || left || right || accept || done || click || Input.MouseWheelDelta != 0)
                _entryIdle = 0;

            int dial = 0;
            if ((up && !_wasPadUp) || (down && !_wasPadDown))
            {
                dial = up ? 1 : -1;
                _padRepeat = 0.4;
            }
            else if (up || down)
            {
                _padRepeat -= deltaTime;
                if (_padRepeat <= 0)
                {
                    dial = up ? 1 : -1;
                    _padRepeat = 0.12;
                }
            }
            if (Input.MouseWheelDelta != 0)
                dial = Math.Sign(Input.MouseWheelDelta);
            if (dial != 0)
                entry.Cycle(dial);

            if (left && !_wasPadLeft) entry.Back();
            if (right && !_wasPadRight) entry.Move(1);
            if ((accept && !_wasPadAccept) || (click && !_wasLeftMouse)) entry.Advance();
            if (done && !_wasPadDone) entry.Finish();
        }

        _wasPadUp = up; _wasPadDown = down; _wasPadLeft = left; _wasPadRight = right;
        _wasPadAccept = accept; _wasPadDone = done; _wasLeftMouse = click;

        if (_entryIdle >= EntryTimeout)
            entry.Finish();

        if (entry.IsDone)
        {
            CommitEntry();
            ShowHighScores(leadsToDemo: false);
        }
    }

    // ----- The frame -----

    public void Update(double deltaTime)
    {
        Clock += deltaTime;
        bool start = Input.IsFiring;
        bool startPressed = start && !_wasStartPressed;
        _wasStartPressed = start;
        bool newInput = Input.IsAnyInput && !_wasAnyInputLastFrame;
        _wasAnyInputLastFrame = Input.IsAnyInput;

        switch (State)
        {
            case GameState.Paused:
                Input.TakePointerMove();
                return;

            case GameState.StartScreen:
                UpdateTitleStars(deltaTime);
                if (startPressed)
                {
                    StartNewGame();
                    return;
                }
                _idleTimer = Input.IsAnyInput ? 0 : _idleTimer + deltaTime;
                if (_idleTimer >= AttractIdleDelay)
                    ShowHighScores(leadsToDemo: true);
                return;

            case GameState.HighScores:
                UpdateTitleStars(deltaTime);
                if (startPressed)
                {
                    StartNewGame();
                    return;
                }
                if (newInput)
                {
                    ReturnToTitle();
                    return;
                }
                _tableTimer -= deltaTime;
                if (_tableTimer <= 0)
                {
                    if (_tableLeadsToDemo)
                        StartAttract();
                    else
                        ReturnToTitle();
                }
                return;

            case GameState.EnterInitials:
                UpdateExplosions(deltaTime);
                UpdateInitials(deltaTime);
                return;

            case GameState.GameOver:
                UpdateExplosions(deltaTime);
                HitFlash = Math.Max(0, HitFlash - deltaTime);
                Shake = Math.Max(0, Shake - deltaTime);
                _gameOverTimer -= deltaTime;
                if (_entryPending)
                {
                    if (_gameOverTimer <= GameOverInputDelay - GameOverBeforeEntry)
                        BeginInitials();
                    return;
                }
                if (startPressed && _gameOverTimer <= 0)
                {
                    StartNewGame();
                    return;
                }
                _idleTimer = Input.IsAnyInput ? 0 : _idleTimer + deltaTime;
                if (_idleTimer >= GameOverIdleDelay)
                    ReturnToTitle();
                return;

            case GameState.Attract:
                if (startPressed)
                {
                    StartNewGame();
                    return;
                }
                if (newInput)
                {
                    ReturnToTitle();
                    return;
                }
                _attractTimer -= deltaTime;
                if (_attractTimer <= 0)
                {
                    ReturnToTitle();
                    return;
                }
                break;
        }

        UpdatePlay(deltaTime);
        if (State == GameState.Playing)
            Sound.StartEngine();
    }

    public void Pause()
    {
        if (State == GameState.Playing)
        {
            State = GameState.Paused;
            Sound.StopAllLoops();
        }
    }

    public void Resume()
    {
        if (State == GameState.Paused)
            State = GameState.Playing;
    }

    private void UpdatePlay(double deltaTime)
    {
        BannerTime = Math.Max(0, BannerTime - deltaTime);
        HitFlash = Math.Max(0, HitFlash - deltaTime);
        Shake = Math.Max(0, Shake - deltaTime);
        _hitGrace = Math.Max(0, _hitGrace - deltaTime);
        _fireTimer = Math.Max(0, _fireTimer - deltaTime);

        // Steering and the triggers: the player's, or the demo pilot's.
        bool firing;
        if (State == GameState.Attract)
        {
            var orders = _pilot.Fly(this, deltaTime);
            CrosshairX = MoveToward(CrosshairX, orders.X, 2.4 * deltaTime);
            CrosshairY = MoveToward(CrosshairY, orders.Y, 2.4 * deltaTime);
            firing = orders.Fire;
        }
        else
        {
            if (Input.TakePointerMove())
            {
                CrosshairX = Input.PointerX;
                CrosshairY = Input.PointerY;
            }
            CrosshairX += Input.StickX * CrosshairSpeed * deltaTime;
            CrosshairY += Input.StickY * CrosshairSpeed * deltaTime;
            firing = Input.IsFiring;
        }
        CrosshairX = Math.Clamp(CrosshairX, -1, 1);
        CrosshairY = Math.Clamp(CrosshairY, -1, 1);

        bool firePressed = firing && !_wasFiring;
        _wasFiring = firing;

        switch (Phase)
        {
            case Phase.Space: UpdateSpace(deltaTime); break;
            case Phase.Surface: UpdateSurface(deltaTime); break;
            case Phase.Trench: UpdateTrench(deltaTime, firePressed); break;
            case Phase.Escape: UpdateEscape(deltaTime); break;
        }

        if (firing && _fireTimer <= 0 && Phase != Phase.Escape && !(Phase == Phase.Trench && IsPortInRange))
            FireLasers();

        UpdateExplosions(deltaTime);
        foreach (var shot in Lasers)
            shot.Age += deltaTime;
        Lasers.RemoveAll(l => l.Age >= LaserShot.ShowTime);
    }

    private static double MoveToward(double value, double target, double maxStep) =>
        value + Math.Clamp(target - value, -maxStep, maxStep);

    /// <summary>The part of the stick beyond a small dead zone in the middle, -1 to 1.</summary>
    private static double Steer(double axis)
    {
        const double dead = 0.1;
        double a = Math.Abs(axis);
        return a < dead ? 0 : Math.Sign(axis) * (a - dead) / (1 - dead);
    }

    private void ShowBanner(string text, string? sub, double time)
    {
        Banner = text;
        SubBanner = sub;
        BannerTime = time;
    }

    // ----- Guns and targets -----

    /// <summary>Everything the guns can hit right now, with where it is and how big.</summary>
    private IEnumerable<(Thing Target, Vec3 At, double Radius)> Targets()
    {
        foreach (var f in Fireballs) yield return (f, f.Position, f.Radius + 4);
        foreach (var t in Ties) yield return (t, t.Position, t.Radius);
        if (Vader is { IsKnockedOut: false } v) yield return (v, v.Position, v.Radius);
        foreach (var b in Bunkers) yield return (b, b.Position + new Vec3(0, 12, 0), b.Radius);
        foreach (var t in Towers)
            if (t.TopAlive) yield return (t, t.Top, t.Radius);
        foreach (var t in Turrets) yield return (t, t.Position, t.Radius);
    }

    /// <summary>What lies under a point on the screen: the nearest target whose outline covers it.</summary>
    public Thing? TargetAt(Point screen, double slack = 6)
    {
        Thing? best = null;
        double bestDepth = double.MaxValue;
        foreach (var (target, at, radius) in Targets())
        {
            var c = Camera.ToCamera(at);
            if (c.Z < Camera.Near * 2 || c.Z > LaserRange) continue;
            var p = Camera.Project(c);
            double r = Camera.ProjectedSize(radius, c.Z) + slack;
            double dx = p.X - screen.X, dy = p.Y - screen.Y;
            if (dx * dx + dy * dy <= r * r && c.Z < bestDepth)
            {
                bestDepth = c.Z;
                best = target;
            }
        }
        return best;
    }

    private void FireLasers()
    {
        _fireTimer = FireInterval;
        if (Phase == Phase.Trench)
            _shotInTrench = true;

        var aim = CrosshairScreen;
        var hit = TargetAt(aim);
        Lasers.Add(new LaserShot(aim, hit != null));
        Sound.PlayLaser();
        if (hit != null)
            HitTarget(hit);
    }

    private void HitTarget(Thing target)
    {
        switch (target)
        {
            case Fireball f:
                f.IsAlive = false;
                AddScore(Fireball.Points);
                Explosions.Add(Explosion.Small(f.Position, FireballColor));
                Sound.PlayFireballBoom();
                break;

            case TieFighter t:
                t.IsAlive = false;
                AddScore(TieFighter.Points);
                Explosions.Add(Explosion.Fighter(t.Position, TieColor));
                Explosions.Add(Explosion.Small(t.Position, Colors.White));
                Sound.PlayTieBoom();
                if (Phase == Phase.Space)
                    _tiesDestroyed++;
                break;

            case VaderTie v:
                HitVader(v);
                break;

            case Bunker b:
                b.IsAlive = false;
                AddScore(Bunker.Points);
                Explosions.Add(Explosion.Fighter(b.Position, TurretColor));
                Sound.PlayGroundBoom();
                break;

            case Tower t:
                t.TopAlive = false;
                _towerValue += 200;
                _towerTopsDestroyed++;
                AddScore(_towerValue);
                Explosions.Add(Explosion.Fighter(t.Top, TowerColor));
                Sound.PlayTowerBoom();
                break;

            case Turret t:
                t.IsAlive = false;
                AddScore(Turret.Points);
                Explosions.Add(Explosion.Fighter(t.Position, TurretColor));
                Sound.PlayGroundBoom();
                break;
        }

        Fireballs.RemoveAll(f => !f.IsAlive);
        Ties.RemoveAll(t => !t.IsAlive);
        Bunkers.RemoveAll(b => !b.IsAlive);
        Turrets.RemoveAll(t => !t.IsAlive);
    }

    private void AddScore(int points) => Score += points;

    // ----- Enemy fire and the shields -----

    /// <summary>A fireball from somewhere out there, aimed at the ship.</summary>
    private void Shoot(Vec3 from, double speed, double spread = 10)
    {
        var aim = Camera.Position + new Vec3((Random.NextDouble() - 0.5) * spread, (Random.NextDouble() - 0.5) * spread, 0);
        var direction = (aim - from).Normalized();

        // The world streams past the ship, and the shot with it, so it is given the stream's speed
        // forward to cancel that out: relative to the ship it flies straight at its aim point.
        var velocity = direction * speed + new Vec3(0, 0, Speed);
        Fireballs.Add(new Fireball(from, velocity));
        Sound.PlayFireball();
    }

    private double FireballSpeed => 260 + 22 * Math.Min(Wave - 1, 8);

    /// <summary>Moves the fireballs; any that reach the cockpit cost a shield.</summary>
    private void UpdateFireballs(double deltaTime)
    {
        foreach (var f in Fireballs)
        {
            var before = Camera.ToCamera(f.Position);
            f.Update(deltaTime);
            var after = Camera.ToCamera(f.Position);
            if (before.Z > 0 && after.Z <= 2)
            {
                f.IsAlive = false;
                // Where it crossed the cockpit's plane, it either hits the ship or goes past.
                double t = before.Z / Math.Max(before.Z - after.Z, 1e-6);
                double x = before.X + (after.X - before.X) * t, y = before.Y + (after.Y - before.Y) * t;
                if (Math.Abs(x) < 22 && Math.Abs(y) < 18)
                    HitShip();
            }
            else if (after.Length > 4000)
            {
                f.IsAlive = false;
            }
        }
        Fireballs.RemoveAll(f => !f.IsAlive);
    }

    /// <summary>A hit on the X-wing: a shield is gone, and with the last one goes the game.</summary>
    private void HitShip()
    {
        if (_hitGrace > 0 || !IsInPlay) return;
        _hitGrace = HitGrace;
        Shields--;
        HitFlash = 0.45;
        Shake = 0.4;
        Sound.PlayShieldHit();

        if (Shields <= 0)
        {
            EndGame();
            return;
        }
        if (Shields <= 2 && !_shieldsLowWarned)
        {
            _shieldsLowWarned = true;
            Sound.PlayAlarm();
            Sound.Say(Voice.ShieldsLow);
        }
    }

    private void EndGame()
    {
        Shields = 0;
        HitFlash = 1.2;
        Shake = 1.0;
        Explosions.Add(new Explosion(new Vec3(0, 0, 60), Colors.White, 70, 120, 2.2, 30) { MovesWithWorld = false });
        Explosions.Add(new Explosion(new Vec3(0, 0, 60), FireballColor, 50, 90, 2.0, 24) { MovesWithWorld = false });
        Sound.StopAllLoops();
        Sound.PlayDeathStarBoom();

        // A demo that runs out of shields lingers on the explosion, then hands back to the title.
        if (State == GameState.Attract)
        {
            _attractTimer = Math.Min(_attractTimer, 3);
            ClearForEnd();
            return;
        }

        State = GameState.GameOver;
        Sound.Say(Voice.GameOver);
        Banner = SubBanner = null;
        ClearForEnd();
        _gameOverTimer = GameOverInputDelay;
        _idleTimer = 0;
        _entryPending = Table.Qualifies(Score);
    }

    private void ClearForEnd()
    {
        Fireballs.Clear();
        Torpedoes.Clear();
        Lasers.Clear();
    }

    private void UpdateExplosions(double deltaTime)
    {
        foreach (var explosion in Explosions)
            explosion.Update(deltaTime);
        Explosions.RemoveAll(e => e.IsFinished);
    }

    /// <summary>Everything that belongs to the world slides back toward the ship as it flies forward.</summary>
    private void FlowWorld(double deltaTime)
    {
        var shift = new Vec3(0, 0, -Speed * deltaTime);
        foreach (var t in Ties) t.Position += shift;
        foreach (var f in Fireballs) f.Position += shift;
        foreach (var b in Bunkers) b.Position += shift;
        foreach (var t in Towers) t.Position += shift;
        foreach (var t in Turrets) t.Position += shift;
        foreach (var c in Catwalks) c.Position += shift;
        foreach (var e in Explosions)
            if (e.MovesWithWorld) e.Position += shift;
        if (Vader != null) Vader.Position += shift;
    }

    // ----- TIE fighters, in all three phases -----

    private double TieSpeed => 300 + 20 * Math.Min(Wave - 1, 8);
    private double TieFireInterval => Math.Max(0.9, 2.6 - 0.2 * (Wave - 1));

    /// <summary>A TIE appearing far ahead, somewhere it can be seen.</summary>
    private TieFighter SpawnTie(double ahead, double spreadX, double spreadY, double minY = double.MinValue, double maxY = double.MaxValue)
    {
        var p = Camera.Position + new Vec3((Random.NextDouble() - 0.5) * 2 * spreadX, (Random.NextDouble() - 0.5) * 2 * spreadY, ahead);
        p = p with { Y = Math.Clamp(p.Y, minY, maxY) };
        var tie = new TieFighter(p)
        {
            FireTimer = 0.8 + Random.NextDouble() * TieFireInterval,
            Weave = Random.NextDouble() * 10,
            PassPoint = new Vec3((Random.Next(2) * 2 - 1) * (50 + Random.NextDouble() * 120),
                                 (Random.Next(2) * 2 - 1) * (30 + Random.NextDouble() * 90), -150)
        };
        Ties.Add(tie);
        return tie;
    }

    /// <summary>
    /// Flies the TIEs: each makes for a point just beside the ship, weaving, firing as it closes, then
    /// screams past and is gone. Returns how many went past this frame.
    /// </summary>
    private int UpdateTies(double deltaTime, double minY = double.MinValue, double maxY = double.MaxValue)
    {
        int passed = 0;
        foreach (var tie in Ties)
        {
            tie.Weave += deltaTime;
            var rel = tie.Position - Camera.Position;
            var target = Camera.Position + tie.PassPoint + new Vec3(Math.Sin(tie.Weave * 1.7) * 70, Math.Cos(tie.Weave * 1.3) * 40, 0);
            var wanted = (target - tie.Position).Normalized() * TieSpeed;
            if (!tie.Breaking)
                tie.Velocity += (wanted - tie.Velocity) * Math.Min(1, deltaTime * 1.8);

            if (rel.Z < 260 && !tie.Breaking)
            {
                tie.Breaking = true;
                Sound.PlayTieScream();
            }

            tie.Update(deltaTime);
            tie.Position = tie.Position with { Y = Math.Clamp(tie.Position.Y, minY, maxY) };
            tie.Yaw = Math.Atan2(tie.Velocity.X, Math.Max(-tie.Velocity.Z, 1)) * 0.6;
            tie.Bank = Math.Sin(tie.Weave * 1.7) * 0.5;

            tie.FireTimer -= deltaTime;
            if (tie.FireTimer <= 0 && rel.Z > 220 && rel.Z < 1500 && !tie.Breaking)
            {
                tie.FireTimer = TieFireInterval * (0.7 + Random.NextDouble() * 0.6);
                Shoot(tie.Position, FireballSpeed);
            }

            if (rel.Z < -120 || rel.Length > 5000)
            {
                tie.IsAlive = false;
                passed++;
            }
        }
        Ties.RemoveAll(t => !t.IsAlive);
        return passed;
    }
}
