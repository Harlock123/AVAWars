using System;
using System.Linq;
using Avalonia.Media;
using AVAWars.Models;

namespace AVAWars.Engine;

// Phase three: the trench run, Vader's TIE, the exhaust port, and the getaway.
public partial class GameEngine
{
    public const double TrenchHalfWidth = 75;
    public const double TrenchDepth = 120;
    public const double PortHalfSize = 16;
    private const double ShipMargin = 14;
    private const double PortRangeNear = 70;
    private const double PortRangeFar = 430;
    private const double RetryFrom = 0.6;          // a missed run starts again this far down the trench
    private const double EscapeTime = 7.5;

    public double TrenchLength { get; private set; }

    /// <summary>The exhaust port's distance ahead, along the trench.</summary>
    public double PortDistance => TrenchLength - Travelled;

    /// <summary>The port is close enough for a torpedo, and the guns give way to the torpedo launcher.</summary>
    public bool IsPortInRange => Phase == Phase.Trench && PortDistance > PortRangeNear && PortDistance < PortRangeFar && !_portHit;

    public Vec3 PortPosition => new(0, -TrenchDepth, PortDistance);

    public double EscapeClock { get; private set; }
    public bool DeathStarGone => EscapeClock > 3.0;
    public string[] Tally { get; private set; } = Array.Empty<string>();

    private bool _shotInTrench;
    private bool _portHit;
    private bool _vaderSpawned;
    private bool _vaderBeaten;
    private bool _portAnnounced;
    private int _trenchTiesLeft;

    private void StartTrench(string? bonusLine)
    {
        ClearAll();
        Phase = Phase.Trench;
        Speed = 360 + 15 * Math.Min(Wave - 1, 8);
        TrenchLength = 6800 + 700 * Math.Min(Wave - 1, 5);
        Travelled = 0;
        _shotInTrench = false;
        _vaderSpawned = false;
        _vaderBeaten = false;
        _portHit = false;
        _portAnnounced = false;
        Camera.Position = new Vec3(0, -TrenchDepth / 2, 0);
        BuildTrench(0);
        _trenchTiesLeft = 2 + Math.Min(3, Wave / 2);
        _spawnTimer = 2.5;

        ShowBanner("TRENCH RUN", bonusLine ?? "STAY ON TARGET", 2.8);
        Sound.PlayPhaseStart();
        Sound.Say(Voice.Trench);
    }

    /// <summary>Lays out the guns and catwalks from a point in the trench to its end.</summary>
    private void BuildTrench(double from)
    {
        Turrets.Clear();
        Catwalks.Clear();
        double start = Math.Max(from + 700, 900);

        for (double z = start; z < TrenchLength - 500; z += 200 + Random.NextDouble() * 220)
        {
            int side = Random.Next(3) - 1;   // left wall, floor, right wall
            var position = side == 0
                ? new Vec3((Random.NextDouble() - 0.5) * 80, -TrenchDepth + 10, z - from)
                : new Vec3(side * (TrenchHalfWidth - 12), -TrenchDepth + 25 + Random.NextDouble() * (TrenchDepth - 45), z - from);
            Turrets.Add(new Turret(position, -side) { FireTimer = Random.NextDouble() * 2 });
        }

        // Catwalks from the second wave: fly over or under them.
        int catwalks = Wave >= 2 ? Math.Min(10, 2 + Wave) : 0;
        for (int i = 0; i < catwalks; i++)
        {
            double z = start + (TrenchLength - 900 - start) * (i + 0.5) / catwalks;
            if (z < start) continue;
            double y = -TrenchDepth + 30 + Random.NextDouble() * (TrenchDepth - 60);
            Catwalks.Add(new Catwalk(new Vec3(0, y, z - from)));
        }
    }

    private void UpdateTrench(double deltaTime, bool firePressed)
    {
        // The ship slides about inside the trench, but can't leave it.
        var p = Camera.Position;
        p = new Vec3(
            Math.Clamp(p.X + Steer(CrosshairX) * 160 * deltaTime, -TrenchHalfWidth + ShipMargin, TrenchHalfWidth - ShipMargin),
            Math.Clamp(p.Y + Steer(CrosshairY) * 110 * deltaTime, -TrenchDepth + ShipMargin, -ShipMargin),
            0);
        Camera.Position = p;
        Camera.Roll = MoveToward(Camera.Roll, Steer(CrosshairX) * 0.15, deltaTime * 1.5);

        var catwalksBefore = Catwalks.Select(c => c.Position.Z).ToArray();
        Travelled += Speed * deltaTime;
        FlowWorld(deltaTime);

        // Catwalks are checked as they pass the cockpit.
        for (int i = 0; i < Catwalks.Count; i++)
        {
            var c = Catwalks[i];
            if (catwalksBefore[i] > 0 && c.Position.Z <= 0 && Math.Abs(c.Position.Y - p.Y) < Catwalk.HalfThickness + 8)
            {
                HitShip();
                Explosions.Add(Explosion.Fighter(new Vec3(p.X, c.Position.Y, 30), TrenchColor));
            }
        }
        Catwalks.RemoveAll(c => c.Position.Z < -40);

        double interval = Math.Max(1.0, 2.8 - 0.2 * (Wave - 1));
        foreach (var turret in Turrets)
        {
            turret.FireTimer -= deltaTime;
            double z = turret.Position.Z;
            if (turret.FireTimer <= 0 && z > 250 && z < 1300)
            {
                turret.FireTimer = interval * (0.7 + Random.NextDouble() * 0.6);
                Shoot(turret.Position, FireballSpeed * 0.85, 20);
            }
        }
        Turrets.RemoveAll(t => t.Position.Z < -40);

        // TIEs come down the trench in the first half; Vader takes over in the second.
        _spawnTimer -= deltaTime;
        if (_trenchTiesLeft > 0 && _spawnTimer <= 0 && Ties.Count < 2 && Travelled < TrenchLength * 0.45)
        {
            var tie = SpawnTie(1800, 30, 25, -TrenchDepth + 30, -20);
            tie.PassPoint = new Vec3((Random.NextDouble() - 0.5) * 60, 0, -150);
            _trenchTiesLeft--;
            _spawnTimer = 2.5 + Random.NextDouble() * 2;
        }
        if (!_vaderSpawned && Travelled > TrenchLength * 0.5)
            SpawnVader();

        UpdateTies(deltaTime, -TrenchDepth + 20, 40);
        UpdateVader(deltaTime);
        UpdateFireballs(deltaTime);
        UpdateTorpedoes(deltaTime);

        if (!_portAnnounced && PortDistance < 1500)
        {
            _portAnnounced = true;
            Sound.Say(Voice.Port);
        }

        if (IsPortInRange && firePressed && Torpedoes.Count == 0)
            LaunchTorpedo();

        // The port went by without a hit: round again for another run, a shield the poorer.
        if (!_portHit && PortDistance < PortRangeNear * 0.5 && Torpedoes.Count == 0 && IsInPlay)
            MissedPort();
    }

    private void SpawnVader()
    {
        _vaderSpawned = true;
        int hits = Math.Min(12, 4 + Wave);
        Vader = new VaderTie(Camera.Position + new Vec3(0, 10, 1100), hits) { FireTimer = 1.5 };
        Sound.PlayVaderScream();
        Sound.Say(Voice.Vader);
    }

    // Vader sits a little way ahead, sliding about the trench and firing, until he's knocked away.
    private void UpdateVader(double deltaTime)
    {
        var v = Vader;
        if (v == null) return;
        v.HitFlash = Math.Max(0, v.HitFlash - deltaTime);

        if (v.IsKnockedOut)
        {
            v.SpinOut += deltaTime;
            v.Position += new Vec3(Math.Sin(v.SpinOut * 9) * 30 * deltaTime, 260 * deltaTime, (Speed + 300) * deltaTime);
            if (v.SpinOut > 2.5)
                Vader = null;
            return;
        }

        // He keeps pace with the ship a little way ahead, closing in to his chosen distance.
        v.Weave += deltaTime;
        var hold = new Vec3(
            Math.Sin(v.Weave * 1.3) * 40 - Camera.Position.X,
            -TrenchDepth / 2 + Math.Sin(v.Weave * 0.9) * 30 - Camera.Position.Y,
            Math.Max(280, 700 - v.Weave * 160));
        var rel = v.Position - Camera.Position;
        rel += (hold - rel) * Math.Min(1, deltaTime * 1.5);
        v.Position = Camera.Position + rel;

        // Near the port he pulls up and out of the way.
        if (PortDistance < 900)
        {
            v.SpinOut = 0;
            return;
        }

        v.FireTimer -= deltaTime;
        if (v.FireTimer <= 0)
        {
            v.FireTimer = Math.Max(0.7, 1.4 - 0.1 * (Wave - 1)) * (0.8 + Random.NextDouble() * 0.4);
            Shoot(v.Position, FireballSpeed * 1.1, 12);
        }
    }

    private void HitVader(VaderTie v)
    {
        AddScore(VaderTie.PointsPerHit);
        v.HitFlash = 0.15;
        v.HitsLeft--;
        Sound.PlayVaderHit();
        Explosions.Add(Explosion.Small(v.Position, VaderColor));
        if (v.HitsLeft <= 0)
        {
            v.SpinOut = 0;
            _vaderBeaten = true;
            Sound.PlayVaderScream();
            Sound.Say(Voice.VaderGone);
            ShowBanner("VADER KNOCKED OUT OF THE TRENCH", null, 2.2);
        }
    }

    private void LaunchTorpedo()
    {
        // The torpedo goes where the crosshair is; it finds the port if the crosshair is on it.
        var port = Camera.ToCamera(PortPosition);
        var portScreen = Camera.Project(port);
        double reach = Camera.ProjectedSize(PortHalfSize, port.Z) + 26;
        var aim = CrosshairScreen;
        double dx = aim.X - portScreen.X, dy = aim.Y - portScreen.Y;
        bool onTarget = dx * dx + dy * dy <= reach * reach;

        Torpedoes.Add(new Torpedo(Camera.Position + new Vec3(0, -6, 10), PortPosition, 0.6, onTarget));
        Sound.PlayTorpedo();
    }

    private void UpdateTorpedoes(double deltaTime)
    {
        // A copy: a hit starts the getaway, which clears the list.
        foreach (var t in Torpedoes.ToList())
        {
            t.Age += deltaTime;
            // It homes on the port if it was aimed true, and sails past it if not.
            var end = t.OnTarget ? PortPosition : PortPosition + new Vec3(CrosshairX * 60, 40, 80);
            t.Position = t.Start + (end - t.Start) * t.Progress + new Vec3(0, Math.Sin(t.Progress * Math.PI) * 25, 0);
            if (t.Progress >= 1)
            {
                t.IsAlive = false;
                if (t.OnTarget)
                    PortDestroyed();
                else
                {
                    Explosions.Add(Explosion.Small(t.Position, Colors.White));
                    Sound.PlayMiss();
                }
            }
        }
        Torpedoes.RemoveAll(t => !t.IsAlive);
    }

    private void MissedPort()
    {
        Sound.PlayMiss();
        Sound.Say(Voice.Missed);
        HitShip();
        if (!IsInPlay || State == GameState.GameOver || Shields <= 0) return;

        // Round again from further down the trench.
        Travelled = TrenchLength * RetryFrom;
        Fireballs.Clear();
        Ties.Clear();
        Torpedoes.Clear();
        BuildTrench(Travelled);
        _portAnnounced = false;
        // Vader comes back for the second run, unless he has already been seen off.
        Vader = null;
        _vaderSpawned = _vaderBeaten;
        ShowBanner("TORPEDOES MISSED", "COME AROUND FOR ANOTHER RUN", 2.5);
    }

    private void PortDestroyed()
    {
        _portHit = true;
        Explosions.Add(Explosion.Big(PortPosition, Colors.White));
        Explosions.Add(Explosion.Big(PortPosition, FireballColor));

        AddScore(25000);
        int force = _shotInTrench ? 0 : ForceBonus;
        if (force > 0)
            AddScore(force);
        bool shieldBack = Shields < MaxShields;
        if (shieldBack)
            Shields++;

        Tally = new[]
        {
            "EXHAUST PORT HIT      25000",
            force > 0 ? $"USE THE FORCE BONUS   {force}" : "",
            shieldBack ? "SHIELD RESTORED" : ""
        }.Where(s => s.Length > 0).ToArray();

        Sound.PlayBonus();
        Sound.Say(force > 0 ? Voice.Force : Voice.GreatShot);
        StartEscape();
    }

    /// <summary>The bonus for a trench run with no shots but the torpedo, as on the cabinet.</summary>
    private int ForceBonus => Wave switch { 1 => 5000, 2 => 10000, 3 => 25000, 4 => 50000, _ => 100000 };

    private void StartEscape()
    {
        Phase = Phase.Escape;
        EscapeClock = 0;
        Ties.Clear();
        Vader = null;
        Fireballs.Clear();
        Turrets.Clear();
        Catwalks.Clear();
        Torpedoes.Clear();
        Speed = 0;
        Camera.Position = Vec3.Zero;
        Camera.Roll = 0;
        DeathStarPosition = new Vec3(0, -200, 1600);
    }

    // Up and out of the trench, the Death Star falling away behind, and then it goes up.
    private void UpdateEscape(double deltaTime)
    {
        bool wasGone = DeathStarGone;
        EscapeClock += deltaTime;
        DeathStarPosition = new Vec3(0, -200 + EscapeClock * 120, 1600 + EscapeClock * 1300);

        if (DeathStarGone && !wasGone)
        {
            var at = DeathStarPosition;
            Explosions.Add(new Explosion(at, Colors.White, 120, 3200, 4.0, 500) { MovesWithWorld = false });
            Explosions.Add(new Explosion(at, FireballColor, 100, 2400, 3.6, 400) { MovesWithWorld = false });
            Explosions.Add(new Explosion(at, DeathStarColor, 80, 1500, 3.2, 260) { MovesWithWorld = false });
            HitFlash = 0.5;
            Shake = 0.8;
            Sound.PlayDeathStarBoom();
            ShowBanner("THE DEATH STAR IS DESTROYED", null, 4.2);
        }

        if (EscapeClock >= EscapeTime && IsInPlay)
        {
            Wave++;
            StartSpace();
        }
    }
}
