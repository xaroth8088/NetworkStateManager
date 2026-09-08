using System.Collections.Generic;
using Unity.Netcode;

namespace NSM
{
    public struct GameEventsBuffer : INetworkSerializable, IGameEventsBuffer
    {
        private Dictionary<int, HashSet<IGameEvent>> _upcomingEvents;

        private Dictionary<int, HashSet<IGameEvent>> UpcomingEvents
        {
            get => _upcomingEvents ??= new();
        }

        public HashSet<IGameEvent> this[int i]
        {
            get
            {
                // TODO: This approach isn't very memory-friendly.  This can improve a lot if/when things are moved to immutable data structures instead.
                if (UpcomingEvents.TryGetValue(i, out HashSet<IGameEvent> gameEvents))
                {
                    return gameEvents;
                }

                UpcomingEvents[i] = new HashSet<IGameEvent>();

                return UpcomingEvents[i];
            }
            set
            {
                UpcomingEvents[i] = value;
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            if (serializer.IsReader)
            {
                // Clean up before receiving
                UpcomingEvents.Clear();

                // How many keys are coming?
                int keyCount = 0;
                serializer.SerializeValue(ref keyCount);
                if (keyCount < 0 || keyCount > 65536) throw new System.InvalidOperationException("Invalid event tick count.");
                int total = 0;

                // For each key
                for (int i = 0; i < keyCount; i++)
                {
                    // What's the key?
                    int key = 0;
                    serializer.SerializeValue(ref key);
                    if (key < 0 || UpcomingEvents.ContainsKey(key)) throw new System.InvalidOperationException("Invalid or duplicate event tick.");

                    // How many events are coming?
                    int eventCount = 0;
                    serializer.SerializeValue(ref eventCount);
                    if (eventCount < 0 || eventCount > 4096 || (total += eventCount) > 65536)
                        throw new System.InvalidOperationException("Event count exceeds wire budget.");
                    UpcomingEvents[key] = new HashSet<IGameEvent>();

                    // Deserialize each event
                    for (int j = 0; j < eventCount; j++)
                    {
                        GameEventDTO gameEventDTO = new();
                        serializer.SerializeValue(ref gameEventDTO);

                        this[key].Add(gameEventDTO.gameEvent);
                    }
                }
            }
            else if (serializer.IsWriter)
            {
                // Clean up before sending
                Vacuum();
                if (UpcomingEvents.Count > 65536 || EventCount > 65536)
                    throw new System.InvalidOperationException("Event buffer exceeds wire budget.");

                // TODO: there's probably a more bandwidth-efficient way to do this

                // How many keys are coming?
                int keyCount = UpcomingEvents.Keys.Count;
                serializer.SerializeValue(ref keyCount);

                // For each key
                foreach (int key in UpcomingEvents.Keys)
                {
                    int _key = key;

                    // Send the key
                    serializer.SerializeValue(ref _key);

                    // How many events are coming?
                    int eventCount = UpcomingEvents[key].Count;
                    serializer.SerializeValue(ref eventCount);

                    // Serialize each event
                    foreach (IGameEvent gameEvent in UpcomingEvents[key])
                    {
                        GameEventDTO gameEventDTO = new()
                        {
                            gameEvent = gameEvent
                        };
                        serializer.SerializeValue(ref gameEventDTO);
                    }
                }
            }
        }

        private void Vacuum()
        {
            // Remove any keys that have empty events lists
            List<int> keys = new(UpcomingEvents.Keys);
            foreach (int key in keys)
            {
                if (UpcomingEvents[key].Count == 0)
                {
                    UpcomingEvents.Remove(key);
                }
            }

        }

        public int Count => UpcomingEvents.Count;
        public int EventCount
        {
            get { int count = 0; foreach (var events in UpcomingEvents.Values) count += events.Count; return count; }
        }

        public void RemoveBefore(int tick)
        {
            var expired = new List<int>();
            foreach (int key in UpcomingEvents.Keys)
                if (key < tick) expired.Add(key);
            foreach (int key in expired) UpcomingEvents.Remove(key);
            Vacuum();
        }
    }
}
