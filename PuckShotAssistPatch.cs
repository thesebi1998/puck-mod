using HarmonyLib;
using UnityEngine;

namespace MyPuckMod
{
    /// <summary>
    /// Präziser Bot-Schussassist nach echter Stick-Puck-Kollision.
    /// Nahschüsse zielen auf den Mittelpunkt des realen Tor-Trigger-Colliders,
    /// mit flacher Flugbahn und ohne Zufall oder absichtliche Fehlversuche.
    /// </summary>
    [HarmonyPatch(typeof(Puck), "OnCollisionEnter")]
    internal static class PuckShotAssistPatch
    {
        private const float MinimumShotSpeed = 20f;
        private const float MaximumShotSpeed = 30f;

        // At and below this distance we deliberately use zero vertical lift.
        private const float FlatShotDistance = 15f;
        private const float LongShotDistance = 32f;
        private const float LongShotLift = 0.85f;

        private static void Postfix(Puck __instance, Collision collision)
        {
            Stick stick;
            if (!collision.gameObject.TryGetComponent<Stick>(out stick))
            {
                return;
            }

            Player shooter = stick.Player;
            if (!shooter || shooter.GetComponent<BotInputDriver>() == null)
            {
                return;
            }

            if (shooter.Team != PlayerTeam.Red && shooter.Team != PlayerTeam.Blue)
            {
                return;
            }

            Goal goal = FindOpponentGoal(shooter.Team);
            if (!goal)
            {
                return;
            }

            Vector3 puckPosition = __instance.transform.position;
            Vector3 target = GetSafeNetTarget(goal, puckPosition);
            Vector3 toTarget = target - puckPosition;
            float flatDistance = new Vector2(toTarget.x, toTarget.z).magnitude;

            if (flatDistance < 0.01f)
            {
                return;
            }

            // Direction in the ice plane always goes through the centre of
            // the actual scoring collider. Near range has exactly zero lift.
            Vector3 flatDirection = new Vector3(toTarget.x, 0f, toTarget.z).normalized;
            float liftT = Mathf.InverseLerp(FlatShotDistance, LongShotDistance, flatDistance);
            float lift = Mathf.Lerp(0f, LongShotLift, liftT);
            Vector3 direction = (flatDirection + Vector3.up * lift).normalized;

            Vector3 bodyForward = shooter.PlayerBody
                ? shooter.PlayerBody.transform.forward
                : flatDirection;
            bodyForward.y = 0f;
            bodyForward = bodyForward.sqrMagnitude > 0.01f
                ? bodyForward.normalized
                : flatDirection;

            float alignment = Vector3.Dot(bodyForward, flatDirection);
            float strength = Mathf.Clamp01((alignment + 0.70f) / 1.70f);
            float speed = Mathf.Lerp(MinimumShotSpeed, MaximumShotSpeed, strength);

            __instance.Rigidbody.linearVelocity = direction * speed;
        }

        // Uses the actual trigger collider's world-space bounds. The target
        // is centered horizontally and placed at 35% of its height: safely
        // above ice level, below the crossbar, and away from both posts.
        private static Vector3 GetSafeNetTarget(Goal goal, Vector3 puckPosition)
        {
            GoalTrigger trigger = goal.GetComponentInChildren<GoalTrigger>(true);
            Collider triggerCollider = trigger
                ? trigger.GetComponent<Collider>()
                : null;

            if (!triggerCollider)
            {
                return goal.transform.position;
            }

            Bounds bounds = triggerCollider.bounds;
            Vector3 target = bounds.center;
            target.y = bounds.min.y + bounds.size.y * 0.35f;

            // Keep the target on the entrance-facing side of a deep trigger
            // volume. The puck-to-target side is determined dynamically.
            Vector3 horizontal = target - puckPosition;
            horizontal.y = 0f;
            if (horizontal.sqrMagnitude > 0.0001f)
            {
                float halfDepth = Mathf.Min(bounds.extents.x, bounds.extents.z);
                target -= horizontal.normalized * halfDepth * 0.45f;
            }

            return target;
        }

        private static Goal FindOpponentGoal(PlayerTeam team)
        {
            Goal[] goals = Object.FindObjectsByType<Goal>(FindObjectsSortMode.None);
            for (int i = 0; i < goals.Length; i++)
            {
                if (goals[i].Team != team)
                {
                    return goals[i];
                }
            }
            return null;
        }
    }
}
