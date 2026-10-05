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
        private float _brakeStartSpeed;

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

        /// <summary>+1 turning left (counter-clockwise), -1 right, 0 about straight.</summary>
        public int TurnSide { get; private set; }

        public Vector2 Forward => new Vector2(MathF.Cos(Heading), MathF.Sin(Heading));
        public Vector2 Velocity => Forward * Speed;

        /// <summary>Scales cruise and max speed (Lighter Frame).</summary>
        public float SpeedScale { get; set; } = 1f;

        /// <summary>Scales the speed lost to hard turns (Better Brakes).</summary>
        public float TurnLossScale { get; set; } = 1f;

        /// <summary>While braking, the speed bonus stays at the speed the brake started from (Better Brakes).</summary>
        public bool KeepBonusWhileBraking { get; set; }

        public float MaxSpeed => _t.maxSpeed * SpeedScale;
        public float CruiseSpeed => _t.cruiseSpeed * SpeedScale;

        /// <summary>The speed the bonus is worked out from.</summary>
        public float BonusSpeed => Braking && KeepBonusWhileBraking ? MathF.Max(Speed, _brakeStartSpeed) : Speed;

        /// <summary>Damage × (1 + v / vmax × bonus).</summary>
        public float DamageMultiplier => 1f + BonusSpeed / MaxSpeed * _t.speedDamageBonus;

        /// <summary>Above this speed contact hits can be dodged.</summary>
        public bool CanDodge => BonusSpeed > _t.dodgeSpeedFraction * MaxSpeed;

        /// <summary>Radians per second at the current speed.</summary>
        public float TurnRate => _t.turnRateAtCruise * Deg2Rad *
            Math.Clamp(CruiseSpeed / Speed, _t.minTurnScale, _t.maxTurnScale);

        public void Step(float dt, Vector2 stick)
        {
            float push = stick.Length();
            float target = CruiseSpeed;
            bool wasBraking = Braking;
            Braking = false;
            TurnAmount = 0f;
            TurnSide = 0;

            if (push > _t.stickDeadZone)
            {
                float delta = WrapAngle(MathF.Atan2(stick.Y, stick.X) - Heading);
                Braking = MathF.Abs(delta) > _t.brakeAngle * Deg2Rad;
                if (!Braking)
                {
                    float t = Math.Clamp((push - _t.stickDeadZone) / (1f - _t.stickDeadZone), 0f, 1f);
                    target = CruiseSpeed + (MaxSpeed - CruiseSpeed) * t;
                }
                else if (!wasBraking)
                {
                    _brakeStartSpeed = Speed;
                }

                float maxTurn = TurnRate * dt;
                float turn = Math.Clamp(delta, -maxTurn, maxTurn);
                Heading = WrapAngle(Heading + turn);
                TurnAmount = MathF.Abs(turn) / maxTurn;
                TurnSide = TurnAmount > 0.05f ? Math.Sign(turn) : 0;
            }

            if (Braking)
            {
                Speed -= _t.brakeDeceleration * dt;
            }
            else
            {
                target -= _t.turnSpeedLoss * TurnLossScale * TurnAmount;
                Speed = MoveTowards(Speed, target, _t.acceleration * dt);
            }

            Speed = Math.Clamp(Speed, _t.minSpeed, MaxSpeed);
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
