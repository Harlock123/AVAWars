using System;
using AVAWars.Models;

namespace AVAWars.Engine;

// Phase two: skimming the Death Star's surface, over laser bunkers and between the towers.
public partial class GameEngine
{
    public const double SurfaceMinHeight = 28;
    public const double SurfaceMaxHeight = 190;
    public const double SurfaceHalfWidth = 420;
    private const double SurfaceLength = 7200;

    /// <summary>How far the ship has flown over the surface, which also slides the grid beneath it.</summary>
    public double Travelled { get; private set; }

    public double SurfaceLeft => Math.Max(0, SurfaceLength - Travelled);
    public int TowersTotal { get; private set; }
    public int TowerTopsDestroyed => _towerTopsDestroyed;

    private int _towerTopsDestroyed;
    private int _towerValue;
    private int _surfaceTiesLeft;

    private void StartSurface()
    {
        ClearAll();
        Phase = Phase.Surface;
        Camera.Position = new Vec3(0, 90, 0);
        Speed = 320 + 15 * Math.Min(Wave - 1, 8);
        Travelled = 0;
        _towerValue = 0;
        _towerTopsDestroyed = 0;
        _surfaceTiesLeft = 2 + Math.Min(4, Wave / 2);
        _spawnTimer = 4;

        // Bunkers scattered along the way; from the second wave, towers too.
        for (double z = 900; z < SurfaceLength - 400; z += 260 + Random.NextDouble() * 240)
            Bunkers.Add(new Bunker(new Vec3((Random.NextDouble() - 0.5) * 2 * (SurfaceHalfWidth - 40), 0, z))
            {
                FireTimer = Random.NextDouble() * 2
            });

        TowersTotal = Wave >= 2 ? Math.Min(12, 3 + Wave) : 0;
        for (int i = 0; i < TowersTotal; i++)
        {
            double z = 1400 + (SurfaceLength - 2000) * (i + Random.NextDouble() * 0.6) / TowersTotal;
            double x = (Random.NextDouble() - 0.5) * 2 * (SurfaceHalfWidth - 60);
            Towers.Add(new Tower(new Vec3(x, 0, z), 110 + Random.NextDouble() * 110, 16 + Random.NextDouble() * 8)
            {
                FireTimer = 1 + Random.NextDouble() * 2
            });
        }

        ShowBanner("DEATH STAR SURFACE", TowersTotal > 0 ? "SHOOT THE TOWER TOPS" : "LASER BUNKERS AHEAD", 2.8);
        Sound.PlayPhaseStart();
        Sound.Say(TowersTotal > 0 ? Voice.Towers : Voice.Surface);
    }

    private void UpdateSurface(double deltaTime)
    {
        // The stick slides the ship across and up and down, and banks it as it goes.
        var p = Camera.Position;
        p = new Vec3(
            Math.Clamp(p.X + Steer(CrosshairX) * 300 * deltaTime, -SurfaceHalfWidth, SurfaceHalfWidth),
            Math.Clamp(p.Y + Steer(CrosshairY) * 170 * deltaTime, SurfaceMinHeight, SurfaceMaxHeight),
            0);
        Camera.Position = p;
        Camera.Roll = MoveToward(Camera.Roll, Steer(CrosshairX) * 0.4, deltaTime * 1.5);

        // Towers are checked as they pass the cockpit: fly into one and it costs a shield.
        var before = new double[Towers.Count];
        for (int i = 0; i < Towers.Count; i++)
            before[i] = Towers[i].Position.Z;

        Travelled += Speed * deltaTime;
        FlowWorld(deltaTime);

        for (int i = 0; i < Towers.Count; i++)
        {
            var tower = Towers[i];
            if (before[i] > 0 && tower.Position.Z <= 0 &&
                Math.Abs(tower.Position.X - p.X) < tower.HalfWidth + 12 && p.Y < tower.Height + 6)
            {
                HitShip();
                Explosions.Add(Explosion.Big(tower.Position + new Vec3(0, p.Y, 0), TowerColor));
                Sound.PlayTowerBoom();
            }
        }

        // Bunkers and tower guns fire when the ship is in range ahead.
        double interval = Math.Max(1.2, 3.2 - 0.2 * (Wave - 1));
        foreach (var bunker in Bunkers)
        {
            bunker.FireTimer -= deltaTime;
            double z = bunker.Position.Z;
            if (bunker.FireTimer <= 0 && z > 300 && z < 1500)
            {
                bunker.FireTimer = interval * (0.7 + Random.NextDouble() * 0.6);
                Shoot(bunker.Position + new Vec3(0, 14, 0), FireballSpeed * 0.9, 30);
            }
        }
        foreach (var tower in Towers)
        {
            tower.FireTimer -= deltaTime;
            double z = tower.Position.Z;
            if (tower.TopAlive && tower.FireTimer <= 0 && z > 300 && z < 1600)
            {
                tower.FireTimer = interval * (0.8 + Random.NextDouble() * 0.6);
                Shoot(tower.Top, FireballSpeed * 0.9, 24);
            }
        }

        // A few TIEs come over the horizon too.
        _spawnTimer -= deltaTime;
        if (_surfaceTiesLeft > 0 && _spawnTimer <= 0 && Ties.Count < 2 && SurfaceLeft > 2500)
        {
            SpawnTie(1900, 400, 60, SurfaceMinHeight + 40, SurfaceMaxHeight + 120);
            _surfaceTiesLeft--;
            _spawnTimer = 3 + Random.NextDouble() * 3;
        }

        UpdateTies(deltaTime, SurfaceMinHeight + 10, 600);
        UpdateFireballs(deltaTime);

        Bunkers.RemoveAll(b => b.Position.Z < -60);
        Towers.RemoveAll(t => t.Position.Z < -60);

        if (Travelled >= SurfaceLength && IsInPlay)
        {
            if (TowersTotal > 0 && _towerTopsDestroyed == TowersTotal)
            {
                AddScore(Tower.AllTopsBonus);
                Sound.PlayBonus();
                StartTrench($"ALL TOWERS DESTROYED   BONUS {Tower.AllTopsBonus}");
            }
            else
            {
                StartTrench(null);
            }
        }
    }
}
