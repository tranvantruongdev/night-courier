using System;
using System.Numerics;

namespace NightCourier.Core
{
    /// <summary>
    /// The bike: it never stops. The stick sets a steering target; pushing it further asks for more speed
    /// (cruise at a light touch, max at full push), and pulling it back past <see cref="RideTuning.brakeAngle"/>
    /// brakes while the bike swings round. Turning is rate-limited and slower at speed, and hard turns lower
    /// the target speed, so momentum has to be planned.
    /// </summary>
    public sealed class BikeMotor
    {
        private const float Deg2Rad = MathF.PI / 180f;

        private readonly RideTuning _t;

        public BikeMotor(RideTuning tuning, Vector2 position, float headingRadians)
        {
            _t = tuning;
            Position = position;
            Heading = headingRadians;
            Speed = tuning.cruiseSpeed;
        }

        public Vector2 Position { get; private set; }

        /// <summary>Radians, counter-clockwise from +x.</summary>
        public float Heading { get; private set; }

        public float Speed { get; private set; }
        public bool Braking { get; private set; }

        /// <summary>How hard the bike turned last step, 0 (straight) to 1 (full rate).</summary>
        public float TurnAmount { get; private set; }

        public Vector2 Forward => new Vector2(MathF.Cos(Heading), MathF.Sin(Heading));
        public Vector2 Velocity => Forward * Speed;

        /// <summary>Damage × (1 + v / vmax × bonus).</summary>
        public float DamageMultiplier => 1f + Speed / _t.maxSpeed * _t.speedDamageBonus;

        /// <summary>Above this speed contact hits can be dodged.</summary>
        public bool CanDodge => Speed > _t.dodgeSpeedFraction * _t.maxSpeed;

        /// <summary>Radians per second at the current speed.</summary>
        public float TurnRate => _t.turnRateAtCruise * Deg2Rad *
            Math.Clamp(_t.cruiseSpeed / Speed, _t.minTurnScale, _t.maxTurnScale);

        public void Step(float dt, Vector2 stick)
        {
            float push = stick.Length();
            float target = _t.cruiseSpeed;
            Braking = false;
            TurnAmount = 0f;

            if (push > _t.stickDeadZone)
            {
                float delta = WrapAngle(MathF.Atan2(stick.Y, stick.X) - Heading);
                Braking = MathF.Abs(delta) > _t.brakeAngle * Deg2Rad;
                if (!Braking)
                {
                    float t = Math.Clamp((push - _t.stickDeadZone) / (1f - _t.stickDeadZone), 0f, 1f);
                    target = _t.cruiseSpeed + (_t.maxSpeed - _t.cruiseSpeed) * t;
                }

                float maxTurn = TurnRate * dt;
                float turn = Math.Clamp(delta, -maxTurn, maxTurn);
                Heading = WrapAngle(Heading + turn);
                TurnAmount = MathF.Abs(turn) / maxTurn;
            }

            if (Braking)
            {
                Speed -= _t.brakeDeceleration * dt;
            }
            else
            {
                target -= _t.turnSpeedLoss * TurnAmount;
                Speed = MoveTowards(Speed, target, _t.acceleration * dt);
            }

            Speed = Math.Clamp(Speed, _t.minSpeed, _t.maxSpeed);
            Position += Forward * (Speed * dt);
        }

        /// <summary>Angle in (-π, π].</summary>
        public static float WrapAngle(float radians)
        {
            radians %= 2f * MathF.PI;
            if (radians > MathF.PI)
            {
                radians -= 2f * MathF.PI;
            }
            else if (radians <= -MathF.PI)
            {
                radians += 2f * MathF.PI;
            }

            return radians;
        }

        private static float MoveTowards(float current, float target, float maxDelta) =>
            Math.Abs(target - current) <= maxDelta ? target : current + Math.Sign(target - current) * maxDelta;
    }
}
