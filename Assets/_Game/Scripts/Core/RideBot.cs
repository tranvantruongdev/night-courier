using System;
using System.Numerics;

namespace NightCourier.Core
{
    /// <summary>
    /// Autopilot for tests, the smoke run and trailer shots: of 16 directions, ride toward the best one. Drones
    /// ahead are danger (closer weighs more), parcels ahead are worth riding at, and a small cost for turning
    /// keeps it from wobbling. It doesn't aim: whatever the weapons hit on the way is enough.
    /// </summary>
    public static class RideBot
    {
        private const int Directions = 16;
        private const float LookRadius = 7f;
        private const float TurnCost = 0.08f;
        private const float ParcelPull = 0.5f;

        public static Vector2 Steer(Ride ride)
        {
            BikeMotor bike = ride.Bike;
            Swarm swarm = ride.Swarm;
            float bestDanger = float.MaxValue;
            Vector2 best = bike.Forward;

            for (int k = 0; k < Directions; k++)
            {
                float offset = BikeMotor.WrapAngle(k * (2f * MathF.PI / Directions));
                float angle = bike.Heading + offset;
                float cx = MathF.Cos(angle), cy = MathF.Sin(angle);
                float danger = TurnCost * MathF.Abs(offset);

                for (int i = 0; i < swarm.Count; i++)
                {
                    float dx = swarm.X[i] - bike.Position.X, dy = swarm.Y[i] - bike.Position.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > LookRadius * LookRadius || d2 < 1e-6f)
                    {
                        continue;
                    }

                    float d = MathF.Sqrt(d2);
                    float along = (dx * cx + dy * cy) / d;
                    if (along <= 0f)
                    {
                        continue;
                    }

                    danger += along * along / d2;
                }

                Parcels parcels = ride.Parcels;
                for (int i = 0; i < parcels.Count; i++)
                {
                    float dx = parcels.X[i] - bike.Position.X, dy = parcels.Y[i] - bike.Position.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > LookRadius * LookRadius || d2 < 1e-6f)
                    {
                        continue;
                    }

                    float d = MathF.Sqrt(d2);
                    float along = (dx * cx + dy * cy) / d;
                    if (along > 0f)
                    {
                        danger -= ParcelPull * along / d;
                    }
                }

                if (danger < bestDanger)
                {
                    bestDanger = danger;
                    best = new Vector2(cx, cy);
                }
            }

            return best;
        }
    }
}
