using System;
using System.Collections.Generic;
using AVAWars.Models;

namespace AVAWars.Engine;

// Phase one: the dogfight in space, with the Death Star growing ahead.
public partial class GameEngine
{
    public const double DeathStarRadius = 1000;
    private const double TurnRate = 1.15;       // radians per second at full stick
    private const double PitchRate = 0.85;
    private const double ApproachTime = 3.0;

    /// <summary>Distant stars, as directions from the ship. In space they turn as the ship does.</summary>
    public List<Vec3> Stars { get; } = new();

    public Vec3 DeathStarPosition { get; private set; }
    public int TiesDestroyed => _tiesDestroyed;
    public int TiesNeeded { get; private set; }

    /// <summary>Counting down while the Death Star swings into the middle of the view and looms up.</summary>
    public double ApproachTimer { get; private set; }

    private int _tiesDestroyed;
    private double _spawnTimer;

    private void InitStars()
    {
        for (int i = 0; i < 320; i++)
        {
            var dir = new Vec3(Random.NextDouble() * 2 - 1, Random.NextDouble() * 2 - 1, Random.NextDouble() * 2 - 1).Normalized();
            Stars.Add(dir);
        }
    }

    // On the title screen the stars drift slowly round, as if the ship were idling in space.
    private void UpdateTitleStars(double deltaTime)
    {
        for (int i = 0; i < Stars.Count; i++)
            Stars[i] = Stars[i].RotateY(-deltaTime * 0.05);
    }

    private void StartSpace()
    {
        ClearAll();
        Phase = Phase.Space;
        _tiesDestroyed = 0;
        TiesNeeded = Math.Min(16, 6 + 2 * (Wave - 1));
        _spawnTimer = 1.5;
        ApproachTimer = 0;
        DeathStarPosition = new Vec3(900, 350, 9000);
        ShowBanner($"WAVE {Wave}", "TIE FIGHTERS ATTACK", 2.8);
        Sound.PlayPhaseStart();
        Sound.Say(Voice.Fighters);
    }

    private int MaxTies => 3 + Math.Min(3, (Wave - 1) / 2);

    private void UpdateSpace(double deltaTime)
    {
        Camera.Position = Vec3.Zero;

        if (ApproachTimer > 0)
        {
            UpdateApproach(deltaTime);
            return;
        }

        // Steering turns the whole sky round the ship.
        double yaw = Steer(CrosshairX) * TurnRate * deltaTime;
        double pitch = Steer(CrosshairY) * PitchRate * deltaTime;
        Camera.Roll = MoveToward(Camera.Roll, Steer(CrosshairX) * 0.35, deltaTime * 1.5);
        Vec3 Turn(Vec3 v) => v.RotateY(-yaw).RotateX(-pitch);

        for (int i = 0; i < Stars.Count; i++)
            Stars[i] = Turn(Stars[i]);
        foreach (var tie in Ties)
        {
            tie.Position = Turn(tie.Position);
            tie.Velocity = Turn(tie.Velocity);
        }
        foreach (var f in Fireballs)
        {
            f.Position = Turn(f.Position);
            f.Velocity = Turn(f.Velocity);
        }
        foreach (var e in Explosions)
            e.Position = Turn(e.Position);

        // The Death Star draws nearer as the fighters fall.
        double progress = Math.Min(1, (double)_tiesDestroyed / TiesNeeded);
        double distance = 9000 - 4500 * progress;
        DeathStarPosition = Turn(DeathStarPosition).Normalized() * distance;

        // Fighters come in waves of a few at a time until enough have been destroyed.
        _spawnTimer -= deltaTime;
        if (_tiesDestroyed < TiesNeeded && Ties.Count < MaxTies && _spawnTimer <= 0 && BannerTime < 1.5)
        {
            SpawnTie(2000 + Random.NextDouble() * 600, 700, 420);
            _spawnTimer = 0.9 + Random.NextDouble() * 1.2;
        }

        UpdateTies(deltaTime);
        UpdateFireballs(deltaTime);

        if (_tiesDestroyed >= TiesNeeded && Fireballs.Count == 0 && IsInPlay)
        {
            ApproachTimer = ApproachTime;
            ShowBanner("APPROACHING THE DEATH STAR", null, ApproachTime);
            Sound.PlayTieScream();
        }
    }

    // The last fighters break off as the Death Star swings round into the middle and fills the view.
    private void UpdateApproach(double deltaTime)
    {
        ApproachTimer -= deltaTime;
        double t = 1 - ApproachTimer / ApproachTime;
        Camera.Roll = MoveToward(Camera.Roll, 0, deltaTime);

        var direction = DeathStarPosition.Normalized();
        var ahead = new Vec3(0, -0.35, 1).Normalized();
        var blended = (direction * (1 - Math.Min(1, t * 2)) + ahead * Math.Min(1, t * 2)).Normalized();
        DeathStarPosition = blended * (4500 - 3300 * t * t);

        foreach (var tie in Ties)
            tie.Position += new Vec3(0, 400 * deltaTime, 200 * deltaTime);
        UpdateTies(deltaTime);
        UpdateFireballs(deltaTime);

        if (ApproachTimer <= 0)
            StartSurface();
    }
}
