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
        private readonly Transform _root;
        private readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        private int _shown;

        public SwarmView(Transform parent)
        {
            _root = new GameObject("Swarm").transform;
            _root.SetParent(parent, false);
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
                bool hauler = swarm.Kind[i] == EnemyKind.Hauler;
                var sprite = hauler ? NeonArt.Hauler : NeonArt.Scout;
                if (r.sprite != sprite)
                {
                    r.sprite = sprite;
                    r.color = hauler ? Palette.Hauler : Palette.Scout;
                }

                float x = swarm.X[i], y = swarm.Y[i];
                float angle = Mathf.Atan2(bike.y - y, bike.x - x) * Mathf.Rad2Deg;
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
