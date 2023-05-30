using System;
using System.Collections.Generic;

namespace NSM
{
    public class InputsBuffer
    {
        private struct InputWrapper
        {
            public bool localInput;
            public bool serverAuthoritative;
            public IPlayerInput input;
        }

        private readonly Dictionary<int, Dictionary<byte, InputWrapper>> _playerInputs = new();  // Do not use outside of the [] accessor!

        private Dictionary<byte, InputWrapper> this[int tick]
        {
            get
            {
                if (!_playerInputs.ContainsKey(tick))
                {
                    _playerInputs[tick] = new();
                }

                return _playerInputs[tick];
            }
        }

        public Dictionary<byte, IPlayerInput> GetInputsForTick(int tick)
        {
            Dictionary<byte, InputWrapper> inputWrappers = this[tick];

            Dictionary<byte, IPlayerInput> playerInputs = new();
            foreach((byte playerId, InputWrapper inputWrapper) in inputWrappers)
            {
                playerInputs[playerId] = inputWrapper.input;
            }

            return playerInputs;
        }

        public IPlayerInput PredictInput(byte playerId, int tick)
        {
            // TODO: alternate prediction algorithms

            // For now, just return the previous tick's input, if any, or a new blank one if not
            // (and store it so we don't have to make a new one each time)
            Dictionary<byte, InputWrapper> inputWrappers = this[tick - 1];

            if(!inputWrappers.TryGetValue(playerId, out InputWrapper inputWrapper))
            {
                IPlayerInput defaultInput = TypeStore.Instance.CreateBlankPlayerInput();

                this[tick - 1][playerId] = new()
                {
                    input = defaultInput
                };

                return defaultInput;
            }

            return inputWrapper.input;
        }

        public void SetLocalInputs(Dictionary<byte, IPlayerInput> localInputs, int tick)
        {
            foreach ((byte playerId, IPlayerInput playerInput) in localInputs)
            {
                this[tick][playerId] = new()
                {
                    localInput = true,
                    serverAuthoritative = true,
                    input = playerInput
                };
            }
        }

        public Dictionary<byte, IPlayerInput> GetMinimalInputsDiff(int tick)
        {
            Dictionary<byte, InputWrapper> inputWrappersThisFrame = this[tick];
            Dictionary<byte, InputWrapper> inputWrappersPreviousFrame = this[tick - 1];

            // This function collects any local inputs that changed from the previous frame
            // (because anything other than that will be predicted by host/clients when they look at the previous frame
            // and/or previous predictions)
            Dictionary<byte, IPlayerInput> playerInputs = new();
            HashSet<byte> playerIds = new(inputWrappersThisFrame.Keys);
            playerIds.UnionWith(inputWrappersPreviousFrame.Keys);

            foreach(byte playerId in playerIds)
            {
                if(!inputWrappersThisFrame.TryGetValue(playerId, out InputWrapper thisFrameInput ))
                {
                    thisFrameInput = new()
                    {
                        input = TypeStore.Instance.CreateBlankPlayerInput()
                    };
                }

                if (!inputWrappersPreviousFrame.TryGetValue(playerId, out InputWrapper previousFrameInput))
                {
                    previousFrameInput = new()
                    {
                        input = TypeStore.Instance.CreateBlankPlayerInput()
                    };
                }

                if( thisFrameInput.localInput == false && previousFrameInput.localInput == false)
                {
                    // This playerId isn't local, so we shouldn't send the inputs
                    continue;
                }

                if(thisFrameInput.input.Equals(previousFrameInput.input))
                {
                    // Both are the same (and could therefore be predicted), so don't include this playerId in the diff to send
                    continue;
                }

                playerInputs[playerId] = thisFrameInput.input;
            }

            return playerInputs;
        }

        internal void SetPlayerInputsAtTickAndPredictForwardUntil(PlayerInputsDTO playerInputs, int clientTimeTick, int untilTick)
        {
            foreach((byte playerId, IPlayerInput playerInput) in playerInputs.PlayerInputs)
            {
                // If we have a locally authoritative input for this player, skip them
                if (this[clientTimeTick].TryGetValue(playerId, out InputWrapper inputWrapper) && inputWrapper.localInput == true)
                {
                    continue;
                }

                // Set the input at clientTimeTick
                this[clientTimeTick][playerId] = new()
                {
                    input = playerInput,
                    serverAuthoritative = true
                };

                // Then, for each tick from there +1 until untilTick, predict the input if we don't already have an authoritative input
                for(int tick = clientTimeTick + 1; tick <= untilTick; tick++)
                {
                    if (this[clientTimeTick].TryGetValue(playerId, out InputWrapper wrapper) && wrapper.serverAuthoritative == true)
                    {
                        continue;
                    }

                    this[clientTimeTick][playerId] = new()
                    {
                        input = PredictInput(playerId, tick),
                        serverAuthoritative = true
                    };
                }
            }
        }
    }
}
