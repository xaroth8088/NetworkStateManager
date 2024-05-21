using System;

namespace NSM
{
    public class RandomManager
    {
        // TODO: prevent usage of random numbers while applying state - only permit when running simulation (events, pre/post physics)

        private System.Random _random;
        private int randomSeedBase;

        public RandomManager(int _randomSeedBase)
        {
            randomSeedBase = _randomSeedBase;
        }

        internal void ResetRandom(int tick)
        {
            UnityEngine.Random.InitState(randomSeedBase + tick);
            _random = new(randomSeedBase + tick);
        }

        public int GetRandomNext()
        {
            if (_random == null)
            {
                throw new Exception("GetRandomNext() was called before StartNetworkStateManager(), which is not allowed");
            }

            return _random.Next();
        }

        public float GetRandomRange(float minInclusive, float maxInclusive)
        {
            if (_random == null)
            {
                throw new Exception("GetRandomRange() was called before StartNetworkStateManager(), which is not allowed");
            }

            return (float)((_random.NextDouble() * (maxInclusive - minInclusive)) + minInclusive);
        }

        public int GetRandomRange(int minInclusive, int maxExclusive)
        {
            if (_random == null)
            {
                throw new Exception("GetRandomRange() was called before StartNetworkStateManager(), which is not allowed");
            }

            return _random.Next(minInclusive, maxExclusive);
        }
    }
}
