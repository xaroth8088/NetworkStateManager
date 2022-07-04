using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace NSM
{
    public struct StateFrameDTO : INetworkSerializable
    {
        public int gameTick;
        public GameStateDTO state;
        private PhysicsStateDTO _physicsState;
        private Dictionary<byte, PlayerInputDTO> _playerInputs;
        private List<GameEventDTO> _events;

        public PhysicsStateDTO PhysicsState
        {
            get => _physicsState ?? new PhysicsStateDTO();
            set => _physicsState = value;
        }

        public Dictionary<byte, PlayerInputDTO> PlayerInputs
        {
            get => _playerInputs ?? new Dictionary<byte, PlayerInputDTO>();
            set => _playerInputs = value;
        }

        public List<GameEventDTO> Events
        {
            get => _events ?? new List<GameEventDTO>();
            set => _events = value;
        }

        void INetworkSerializable.NetworkSerialize<T>(BufferSerializer<T> serializer)
        {
            if (_playerInputs == null)
            {
                _playerInputs = new Dictionary<byte, PlayerInputDTO>();
            }

            if (_events == null)
            {
                _events = new List<GameEventDTO>();
            }

            if (_physicsState == null)
            {
                _physicsState = new();
            }

            serializer.SerializeValue(ref gameTick);
            serializer.SerializeValue(ref _physicsState);
            serializer.SerializeValue(ref state);
            NetcodeUtils.SerializeList(ref _events, serializer);
            NetcodeUtils.SerializeDictionary(ref _playerInputs, serializer);
        }

        public StateFrameDTO GenerateDelta(StateFrameDTO newerState)
        {
            StateFrameDTO deltaState = new();

            deltaState.gameTick = newerState.gameTick;
            deltaState.PhysicsState = deltaState.PhysicsState.GenerateDelta(newerState.PhysicsState);

            deltaState._playerInputs = new();
            foreach (KeyValuePair<byte, PlayerInputDTO> entry in newerState.PlayerInputs)
            {
                if (_playerInputs != null && _playerInputs.GetValueOrDefault(entry.Key, new PlayerInputDTO()).Equals(entry.Value))
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
            state = deltaState.state;

            PhysicsState.ApplyDelta(deltaState.PhysicsState);

            foreach (KeyValuePair<byte, PlayerInputDTO> entry in deltaState.PlayerInputs)
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
            newFrame.state = state;
            newFrame.PhysicsState = PhysicsState;
            newFrame.PlayerInputs = new Dictionary<byte, PlayerInputDTO>(PlayerInputs);
            newFrame.Events = new List<GameEventDTO>(Events);

            return newFrame;
        }
    }
}