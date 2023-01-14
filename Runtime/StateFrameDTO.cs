using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;

namespace NSM
{
    public struct StateFrameDTO : INetworkSerializable
    {
        public int gameTick;
        public IGameState gameState;
        private PhysicsStateDTO _physicsState;
        private Dictionary<byte, IPlayerInput> _playerInputs;
        private List<IGameEvent> _events;

        public PhysicsStateDTO PhysicsState
        {
            get => _physicsState ?? new PhysicsStateDTO();
            set => _physicsState = value;
        }

        public Dictionary<byte, IPlayerInput> PlayerInputs
        {
            get => _playerInputs ?? new Dictionary<byte, IPlayerInput>();
            set => _playerInputs = value;
        }

        public List<IGameEvent> Events
        {
            get => _events ?? new List<IGameEvent>();
            set => _events = value;
        }

        void INetworkSerializable.NetworkSerialize<T>(BufferSerializer<T> serializer)
        {
            // We might not have some of these fields either because this frame simply doesn't have a part of it,
            // or because the serializer is reading in data and there's no default created yet.
            // Either way, create default versions of the objects for use in the serialization process.
            if (_playerInputs == null)
            {
                _playerInputs = new Dictionary<byte, IPlayerInput>();
            }

            if (_events == null)
            {
                _events = new List<IGameEvent>();
            }

            if (_physicsState == null)
            {
                _physicsState = new();
            }

            if (gameState == null)
            {
                Type gameStateType = TypeStore.Instance.GameStateType;
                gameState = (IGameState)Activator.CreateInstance(gameStateType);
            }

            serializer.SerializeValue(ref gameTick);
            serializer.SerializeValue(ref _physicsState);
            gameState.NetworkSerialize(serializer);
            SerializeGameEvents<T>(ref _events, serializer);
            SerializePlayerInputs<T>(ref _playerInputs, serializer);
        }

        private void SerializeGameEvents<T>(ref List<IGameEvent> gameEvents, BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            if (serializer.IsReader)
            {
                // Read the length of the list
                byte length = 0;
                serializer.SerializeValue(ref length);

                // Set up our interim storage for the data
                IGameEvent[] values = new IGameEvent[length];

                // Fill the values array with concrete instances, so we can tell them to serialize later
                Type gameEventType = TypeStore.Instance.GameEventType;
                IGameEvent defaultGameEvent = (IGameEvent)Activator.CreateInstance(gameEventType);
                Array.Fill(values, defaultGameEvent);

                // Read in the values
                for (byte i = 0; i < length; i++)
                {
                    values[i].NetworkSerialize(serializer);
                }

                _events = values.ToList();
            }
            else if (serializer.IsWriter)
            {
                // Write the length of the list
                byte length = (byte)gameEvents.Count;
                serializer.SerializeValue(ref length);

                // Write the data
                foreach (IGameEvent gameEvent in gameEvents)
                {
                    gameEvent.NetworkSerialize(serializer);
                }
            }
        }

        private void SerializePlayerInputs<T>(ref Dictionary<byte, IPlayerInput> playerInputs, BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            if (serializer.IsReader)
            {
                // Read the length of the dictionary
                byte length = 0;
                serializer.SerializeValue(ref length);

                // Set up our interim storage for the data
                byte[] keys = new byte[length];
                IPlayerInput[] values = new IPlayerInput[length];

                // Fill the values array with concrete instances, so we can tell them to serialize later
                Type playerInputType = TypeStore.Instance.PlayerInputType;
                IPlayerInput defaultPlayerInput = (IPlayerInput)Activator.CreateInstance(playerInputType);
                Array.Fill(values, defaultPlayerInput);

                // Read the data
                for (byte i = 0; i < length; i++)
                {
                    serializer.SerializeValue(ref keys[i]);
                    values[i].NetworkSerialize(serializer);
                }

                // Construct the output dictionary and set it
                playerInputs = Enumerable.Range(0, keys.Length).ToDictionary(i => keys[i], i => values[i]);
            }
            else if (serializer.IsWriter)
            {
                // Write the length of the dictionary
                byte length = (byte)playerInputs.Count;
                serializer.SerializeValue(ref length);

                // Write the data
                foreach (KeyValuePair<byte, IPlayerInput> item in playerInputs)
                {
                    byte key = item.Key;
                    serializer.SerializeValue(ref key);
                    item.Value.NetworkSerialize(serializer);
                }
            }
        }

        public StateFrameDTO GenerateDelta(StateFrameDTO newerState)
        {
            StateFrameDTO deltaState = new();

            deltaState.gameTick = newerState.gameTick;
            deltaState.PhysicsState = deltaState.PhysicsState.GenerateDelta(newerState.PhysicsState);

            deltaState._playerInputs = new();
            foreach (KeyValuePair<byte, IPlayerInput> entry in newerState.PlayerInputs)
            {
                Type playerInputType = TypeStore.Instance.PlayerInputType;
                IPlayerInput defaultInput = (IPlayerInput)Activator.CreateInstance(playerInputType);

                if (_playerInputs != null && _playerInputs.GetValueOrDefault(entry.Key, defaultInput).Equals(entry.Value))
                {
                    continue;
                }

                deltaState._playerInputs[entry.Key] = entry.Value;
            }

            deltaState.Events = Events;

            return deltaState;
        }

        public void ApplyDelta(StateFrameDTO deltaState)
        {
            gameTick = deltaState.gameTick;
            gameState = deltaState.gameState;

            PhysicsState.ApplyDelta(deltaState.PhysicsState);

            foreach (KeyValuePair<byte, IPlayerInput> entry in deltaState.PlayerInputs)
            {
                _playerInputs[entry.Key] = entry.Value;
            }

            _events = deltaState.Events;
        }

        public StateFrameDTO Duplicate()
        {
            // TODO: change all mutable collections inside this struct (and its children) to instead use immutable versions,
            //       so that we don't need to do this (and can avoid other sneaky bugs down the line)
            StateFrameDTO newFrame = new();

            newFrame.gameTick = gameTick;
            newFrame.gameState = gameState;
            newFrame.PhysicsState = PhysicsState;
            newFrame.PlayerInputs = new Dictionary<byte, IPlayerInput>(PlayerInputs);
            newFrame.Events = new List<IGameEvent>(Events);

            return newFrame;
        }
    }
}