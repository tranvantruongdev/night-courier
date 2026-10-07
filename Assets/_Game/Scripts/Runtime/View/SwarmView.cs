using System.Collections.Generic;
using NightCourier.Art;
using NightCourier.Core;
using UnityEngine;

namespace NightCourier.View
{
    /// <summary>
    /// Draws the swarm from its arrays: one pooled SpriteRenderer per alive slot, positioned in a single loop.
    /// No MonoBehaviour or Update per drone; all drones share the default sprite material, so they batch.
    /// </summary>
    public sealed class SwarmView
    {
        private static readonly Vector3 EliteScale = Vector3.one * 2.3f;
        private static readonly Vector3 SplitterScale = Vector3.one * 1.35f;
        private static readonly Vector3 ZapperScale = Vector3.one * 0.65f;
        private static readonly Vector3 FreightScale = Vector3.one * 2.55f; // 1.4 u
        private static readonly Vector3 BossScale = Vector3.one * 2.9f; // hauler art (0.55 u) to the boss's 1.6 u

        private readonly SpriteRenderer[] _orbs = new SpriteRenderer[Ride.MaxOrbs];
        private const float PopSeconds = 0.25f;
        private readonly SpriteRenderer[] _pops = new SpriteRenderer[32];
        private readonly float[] _popAge = new float[32];
        private int _nextPop;

        private readonly Transform _root;
        private readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        private int _shown;

        public SwarmView(Transform parent)
        {
            _root = new GameObject("Swarm").transform;
            _root.SetParent(parent, false);
            for (int i = 0; i < _orbs.Length; i++)
            {
                var go = new GameObject("Orb");
                go.transform.SetParent(_root, false);
                go.transform.localScale = Vector3.one * 1.6f; // Dot body radius 0.125 u x 1.6 = the 0.2 u hitbox
                _orbs[i] = go.AddComponent<SpriteRenderer>();
                _orbs[i].sprite = NeonArt.Dot;
                _orbs[i].color = Palette.Orb;
                _orbs[i].sortingOrder = 13; // above everything but the HUD: you have to see what's coming
                _orbs[i].enabled = false;
            }

            for (int i = 0; i < _pops.Length; i++)
            {
                var go = new GameObject("Pop");
                go.transform.SetParent(_root, false);
                _pops[i] = go.AddComponent<SpriteRenderer>();
                _pops[i].sprite = NeonArt.Dot;
                _pops[i].sortingOrder = 6;
                _pops[i].enabled = false;
            }
        }

        private static Color ColorOf(EnemyKind kind) => kind switch
        {
            EnemyKind.Hauler => Palette.Hauler,
            EnemyKind.Elite => Palette.Elite,
            EnemyKind.Splitter => Palette.Splitter,
            EnemyKind.Zapper => Palette.Zapper,
            EnemyKind.Boss => Palette.Boss,
            EnemyKind.Freight => Palette.Freight,
            _ => Palette.Scout,
        };

        /// <summary>A quick flash where a drone went down, in its colour.</summary>
        public void Pop(Vector2 at, EnemyKind kind)
        {
            int i = _nextPop;
            _nextPop = (_nextPop + 1) % _pops.Length;
            _pops[i].transform.position = at;
            _pops[i].color = ColorOf(kind);
            _popAge[i] = 0f;
            _pops[i].enabled = true;
        }

        private void AgePops(float dt)
        {
            for (int i = 0; i < _pops.Length; i++)
            {
                if (!_pops[i].enabled)
                {
                    continue;
                }

                _popAge[i] += dt;
                float t = _popAge[i] / PopSeconds;
                _pops[i].enabled = t < 1f;
                _pops[i].transform.localScale = Vector3.one * (0.6f + 1.6f * t);
                var c = _pops[i].color;
                c.a = 1f - t;
                _pops[i].color = c;
            }
        }

        public void SyncOrbs(Ride ride)
        {
            AgePops(Time.deltaTime);
            for (int o = 0; o < _orbs.Length; o++)
            {
                bool live = ride.OrbLife[o] > 0f;
                _orbs[o].enabled = live;
                if (live)
                {
                    _orbs[o].transform.position = new Vector3(ride.OrbX[o], ride.OrbY[o], 0f);
                }
            }
        }

        public void Sync(Swarm swarm, Vector2 bike)
        {
            while (_pool.Count < swarm.Count)
            {
                var go = new GameObject("Drone");
                go.transform.SetParent(_root, false);
                var r = go.AddComponent<SpriteRenderer>();
                r.sortingOrder = 5;
                _pool.Add(r);
            }

            for (int i = 0; i < swarm.Count; i++)
            {
                var r = _pool[i];
                var kind = swarm.Kind[i];
                bool square = kind == EnemyKind.Hauler || kind == EnemyKind.Zapper || kind == EnemyKind.Boss || kind == EnemyKind.Freight;
                r.sprite = square ? NeonArt.Hauler : NeonArt.Scout;
                r.color = ColorOf(kind);
                r.transform.localScale = kind switch
                {
                    EnemyKind.Elite => EliteScale,
                    EnemyKind.Splitter => SplitterScale,
                    EnemyKind.Zapper => ZapperScale,
                    EnemyKind.Boss => BossScale,
                    EnemyKind.Freight => FreightScale,
                    _ => Vector3.one,
                };

                float x = swarm.X[i], y = swarm.Y[i];
                float angle = kind == EnemyKind.Elite
                    ? swarm.Heading[i] * Mathf.Rad2Deg // elites face where they're going
                    : Mathf.Atan2(bike.y - y, bike.x - x) * Mathf.Rad2Deg;
                r.transform.SetPositionAndRotation(new Vector3(x, y, 0f), Quaternion.Euler(0f, 0f, angle));
                r.enabled = true;
            }

            for (int i = swarm.Count; i < _shown; i++)
            {
                _pool[i].enabled = false;
            }

            _shown = swarm.Count;
        }
    }
}
