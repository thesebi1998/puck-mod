using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MyPuckMod;

/// <summary>
/// Goalie: nutzt ausschliesslich die echte, vom Spiel gelieferte
/// PlayerPosition (context.HomePosition). Kein Goal.transform-Pivot,
/// keine Vorhersagefahrt und kein Kreisen um das Tor.
/// </summary>
public class TeamGoalieBrain : BotBrain
{
    private const float GoalDepth = 2.10f;
    private const float SnapBackDistance = 1.25f;

    public override void Tick(in BotContext context, BotInputDriver driver)
    {
        Player goalie = driver.Player;
        PlayerBody body = goalie.PlayerBody;
        Puck puck = context.TargetPuck;

        if (!body || !puck)
        {
            return;
        }

        Goal ownGoal = TeamBotUtils.FindGoalForTeam(goalie.Team);
        Goal opponentGoal = TeamBotUtils.FindOpponentGoal(goalie.Team);

        Vector3 goalieSpot = TeamBotUtils.GetFixedGoalieSpot(
            ownGoal,
            opponentGoal,
            GoalDepth,
            body.transform.position);

        Vector3 flatOffset = body.transform.position - goalieSpot;
        flatOffset.y = 0f;

        // Never pathfind / skate to the goal. If physics displaced the
        // goalie, put him directly back in front of the net instead.
        if (flatOffset.magnitude >= SnapBackDistance)
        {
            body.transform.position = goalieSpot;

            if (body.Rigidbody != null)
            {
                body.Rigidbody.linearVelocity = Vector3.zero;
                body.Rigidbody.angularVelocity = Vector3.zero;
            }
        }

        driver.HoldFacing(puck.transform.position);
        TeamBotUtils.AimStickAtPuck(
            driver,
            puck,
            body.transform.position);
    }
}

public class TeamDefenderBrain : BotBrain
{
    private const float ReturnStartDistance = 1.75f;
    private const float ReturnStopDistance = 0.65f;
    private bool returningHome;

    public override void Tick(in BotContext context, BotInputDriver driver)
    {
        PlayerBody body = driver.Player.PlayerBody;
        Puck puck = context.TargetPuck;
        if (!body || !puck)
        {
            return;
        }

        Vector3 home = context.HasHomePosition
            ? context.HomePosition
            : body.transform.position;
        float distanceHome = TeamBotUtils.FlatDistance(
            body.transform.position, home);

        if (returningHome)
        {
            if (distanceHome <= ReturnStopDistance)
            {
                returningHome = false;
            }
        }
        else if (distanceHome >= ReturnStartDistance)
        {
            returningHome = true;
        }

        if (returningHome)
        {
            driver.MoveToward(home, false);
        }
        else
        {
            driver.HoldFacing(puck.transform.position);
        }

        TeamBotUtils.AimStickAtPuck(driver, puck, body.transform.position);
    }
}

/// <summary>
/// Aggressiver Skater. Nur der naechste Angreifer jagt; der zweite
/// bleibt seitlich als Anspielstation.
/// </summary>
public class TeamAggressiveBrain : BotBrain
{
    private const float SprintMinimumDistance = 4.0f;
    private const float StopDistance = 2.20f;
    private const float SupportDistance = 5.0f;

    public override void Tick(in BotContext context, BotInputDriver driver)
    {
        Player attacker = driver.Player;
        PlayerBody body = attacker.PlayerBody;
        Puck puck = context.TargetPuck;
        if (!body || !puck)
        {
            return;
        }

        bool isAssignedChaser = IsClosestAggressiveBot(attacker, puck);
        Vector3 target;
        bool sprint = false;

        if (isAssignedChaser)
        {
            target = puck.transform.position;
            float distance = TeamBotUtils.FlatDistance(body.transform.position, target);
            if (distance <= StopDistance)
            {
                driver.HoldFacing(target);
                TeamBotUtils.AimStickAtPuck(driver, puck, body.transform.position);
                return;
            }
            sprint = distance >= SprintMinimumDistance && IsSprintBurstActive();
        }
        else
        {
            Goal opponentGoal = TeamBotUtils.FindOpponentGoal(attacker.Team);
            Vector3 puckToGoal = opponentGoal
                ? opponentGoal.transform.position - puck.transform.position
                : Vector3.forward;
            puckToGoal.y = 0f;
            if (puckToGoal.sqrMagnitude < 0.01f)
            {
                puckToGoal = Vector3.forward;
            }

            Vector3 side = Vector3.Cross(Vector3.up, puckToGoal.normalized);
            float sign = attacker.OwnerClientId % 2UL == 0UL ? 1f : -1f;
            target = puck.transform.position + side * sign * SupportDistance;
        }

        driver.MoveToward(target, sprint);
        TeamBotUtils.AimStickAtPuck(driver, puck, body.transform.position);
    }

    private static bool IsClosestAggressiveBot(Player self, Puck puck)
    {
        PlayerManager playerManager = MonoBehaviourSingleton<PlayerManager>.Instance;
        if (!playerManager)
        {
            return true;
        }

        Player closest = null;
        float closestDistance = float.MaxValue;
        foreach (Player player in playerManager.GetPlayers(false))
        {
            if (!TeamBotRegistry.AggressiveIds.Contains(player.OwnerClientId) ||
                !player.PlayerBody || !player.IsCharacterSpawned)
            {
                continue;
            }

            float distance = TeamBotUtils.FlatDistance(
                player.PlayerBody.transform.position,
                puck.transform.position);
            if (distance < closestDistance)
            {
                closest = player;
                closestDistance = distance;
            }
        }
        return closest == self;
    }

    private static bool IsSprintBurstActive()
    {
        return (Time.fixedTime % 1.70f) < 1.10f;
    }
}

internal static class TeamBotUtils
{
    internal static void AimStickAtPuck(
        BotInputDriver driver,
        Puck puck,
        Vector3 botPosition)
    {
        Vector3 toPuck = puck.transform.position - botPosition;
        toPuck.y = 0f;
        driver.AimStickAt(puck, toPuck, toPuck.magnitude);
    }
    internal static Vector3 GetFixedGoalieSpot(
    Goal ownGoal,
    Goal opponentGoal,
    float depth,
    Vector3 fallback)
    {
    if (!ownGoal || !opponentGoal)
    {
        return fallback;
    }

    Vector3 fromOwnGoalToRink =
        opponentGoal.transform.position - ownGoal.transform.position;

    fromOwnGoalToRink.y = 0f;

    if (fromOwnGoalToRink.sqrMagnitude < 0.01f)
    {
        return fallback;
    }

    Vector3 goalieSpot =
        ownGoal.transform.position +
        fromOwnGoalToRink.normalized * depth;

    goalieSpot.y = ownGoal.transform.position.y;

    return goalieSpot;
    }
    internal static Goal FindGoalForTeam(PlayerTeam team)
    {
    Goal[] goals = Object.FindObjectsByType<Goal>(
        FindObjectsSortMode.None);

    return goals.FirstOrDefault(goal => goal.Team == team);
    }
    internal static Goal FindOpponentGoal(PlayerTeam team)
    {
        Goal[] goals = Object.FindObjectsByType<Goal>(FindObjectsSortMode.None);
        return goals.FirstOrDefault(goal => goal.Team != team);
    }

    internal static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
