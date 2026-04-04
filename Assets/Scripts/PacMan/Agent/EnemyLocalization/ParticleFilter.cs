using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PacMan.Agent.EnemyLocalization
{
    [Serializable]
    public class PFParticle
    {
        public Vector3 Position;
        public float Weight;

        public PFParticle(Vector3 position, float weight)
        {
            Position = position;
            Weight = weight;
        }
    }

    [Serializable]
    public class ParticleFilter
    {
        public int ParticleCount = 300;
        public float MotionNoiseStd = 0.35f;
        public float MaxStepDistance = 1.0f;
        public float ResampleJitter = 0.1f;
        public float MinWeight = 1e-6f;
        public float ExactObservationNoise = 0.05f;

        private readonly List<PFParticle> _particles = new List<PFParticle>();
        private readonly System.Random _rng = new System.Random();

        private Bounds _worldBounds;
        private Func<Vector3, bool> _isTraversable;

        public bool IsInitialized => _particles.Count > 0;
        public IReadOnlyList<PFParticle> Particles => _particles;

        public void Initialize(Bounds worldBounds, Func<Vector3, bool> isTraversable, Vector3? initialGuess = null)
        {
            _worldBounds = worldBounds;
            _isTraversable = isTraversable;
            _particles.Clear();

            for (int i = 0; i < ParticleCount; i++)
            {
                Vector3 p;

                if (initialGuess.HasValue)
                {
                    p = initialGuess.Value + SampleGaussianVector(1.0f);
                    p = ClampToBounds(p);

                    if (!IsValid(p))
                        p = SampleRandomValidPoint();
                }
                else
                {
                    p = SampleRandomValidPoint();
                }

                _particles.Add(new PFParticle(p, 1f / ParticleCount));
            }
        }

        public void Predict(Vector3 drift, float dt)
        {
            if (!IsInitialized) return;

            for (int i = 0; i < _particles.Count; i++)
            {
                Vector3 oldPos = _particles[i].Position;

                Vector3 randomDir = UnityEngine.Random.insideUnitSphere;
                randomDir.y = 0f;

                if (randomDir.sqrMagnitude > 1e-6f)
                    randomDir = randomDir.normalized * UnityEngine.Random.Range(0f, MaxStepDistance);

                Vector3 newPos = oldPos + drift * dt + randomDir + SampleGaussianVector(MotionNoiseStd);
                newPos = ClampToBounds(newPos);

                if (IsValid(newPos))
                    _particles[i].Position = newPos;
                else
                    _particles[i].Position = oldPos;
            }
        }

        public void UpdateWithExactObservation(Vector3 exactPosition)
        {
            if (!IsInitialized) return;

            for (int i = 0; i < _particles.Count; i++)
            {
                Vector3 p = exactPosition + SampleGaussianVector(ExactObservationNoise);
                p = ClampToBounds(p);

                if (!IsValid(p))
                    p = exactPosition;

                _particles[i].Position = p;
                _particles[i].Weight = 1f / ParticleCount;
            }
        }

        public void UpdateWithNoisyObservation(Vector3 observedCenter, float readingDispersion)
        {
            if (!IsInitialized) return;

            float halfWidth = readingDispersion * 0.5f;
            float totalWeight = 0f;

            for (int i = 0; i < _particles.Count; i++)
            {
                Vector3 p = _particles[i].Position;

                bool inside =
                    Mathf.Abs(p.x - observedCenter.x) <= halfWidth &&
                    Mathf.Abs(p.z - observedCenter.z) <= halfWidth;

                float w = inside ? 1f : MinWeight;
                _particles[i].Weight = w;
                totalWeight += w;
            }

            NormalizeWeights(totalWeight);
            Resample();
        }

        public Vector3 GetEstimatedPosition()
        {
            if (!IsInitialized || _particles.Count == 0)
                return Vector3.zero;

            Vector3 mean = Vector3.zero;
            float total = 0f;

            for (int i = 0; i < _particles.Count; i++)
            {
                mean += _particles[i].Position * _particles[i].Weight;
                total += _particles[i].Weight;
            }

            if (total <= 1e-8f)
                return _particles[0].Position;

            return mean / total;
        }

        public PFParticle GetBestParticle()
        {
            if (!IsInitialized || _particles.Count == 0)
                return null;

            return _particles.OrderByDescending(p => p.Weight).First();
        }

        private void NormalizeWeights(float totalWeight)
        {
            if (totalWeight <= 1e-12f)
            {
                float uniform = 1f / _particles.Count;
                for (int i = 0; i < _particles.Count; i++)
                    _particles[i].Weight = uniform;
                return;
            }

            for (int i = 0; i < _particles.Count; i++)
                _particles[i].Weight /= totalWeight;
        }

        private void Resample()
        {
            List<PFParticle> newParticles = new List<PFParticle>(_particles.Count);

            float step = 1f / _particles.Count;
            float r = UnityEngine.Random.Range(0f, step);
            float c = _particles[0].Weight;
            int i = 0;

            for (int m = 0; m < _particles.Count; m++)
            {
                float u = r + m * step;

                while (u > c && i < _particles.Count - 1)
                {
                    i++;
                    c += _particles[i].Weight;
                }

                Vector3 p = _particles[i].Position + SampleGaussianVector(ResampleJitter);
                p = ClampToBounds(p);

                if (!IsValid(p))
                    p = _particles[i].Position;

                newParticles.Add(new PFParticle(p, 1f / ParticleCount));
            }

            _particles.Clear();
            _particles.AddRange(newParticles);
        }

        private bool IsValid(Vector3 p)
        {
            if (!_worldBounds.Contains(p))
                return false;

            return _isTraversable == null || _isTraversable(p);
        }

        private Vector3 SampleRandomValidPoint()
        {
            for (int k = 0; k < 1000; k++)
            {
                float x = UnityEngine.Random.Range(_worldBounds.min.x, _worldBounds.max.x);
                float z = UnityEngine.Random.Range(_worldBounds.min.z, _worldBounds.max.z);
                Vector3 p = new Vector3(x, 0f, z);

                if (IsValid(p))
                    return p;
            }

            return _worldBounds.center;
        }

        private Vector3 ClampToBounds(Vector3 p)
        {
            return new Vector3(
                Mathf.Clamp(p.x, _worldBounds.min.x, _worldBounds.max.x),
                0f,
                Mathf.Clamp(p.z, _worldBounds.min.z, _worldBounds.max.z)
            );
        }

        private Vector3 SampleGaussianVector(float std)
        {
            return new Vector3(
                NextGaussian() * std,
                0f,
                NextGaussian() * std
            );
        }

        private float NextGaussian()
        {
            double u1 = 1.0 - _rng.NextDouble();
            double u2 = 1.0 - _rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
        
        public void PruneParticles(System.Func<Vector3, bool> shouldKeep)
        {
            if (!IsInitialized) return;

            float totalWeight = 0f;
            int keptCount = 0;

            for (int i = 0; i < _particles.Count; i++)
            {
                if (!shouldKeep(_particles[i].Position))
                {
                    _particles[i].Weight = 0f;
                }
                else
                {
                    keptCount++;
                }

                totalWeight += _particles[i].Weight;
            }

            // If everything got pruned, do nothing this frame instead of collapsing badly
            if (keptCount == 0 || totalWeight <= 1e-12f)
                return;

            NormalizeWeights(totalWeight);
            Resample();
        }
    }
}