using System.Collections.Generic;
using System.Linq;

namespace NSM
{
    internal struct InputWrapper
    {
        public bool localInput;
        public bool serverAuthoritative;
        public IPlayerInput input;
    }

    internal class InputsBuffer : SortedDefaultDict<int, Dictionary<byte, InputWrapper>>, IInputsBuffer
    {
        public InputsBuffer() : base(() => new()) { }

        /// <summary>
        /// Unwraps the inputs at the given tick, for simpler use
        /// </summary>
        /// <param name="tick">Which tick did you want inputs for?</param>
        /// <returns>A dictionary of playerId:IPlayerInput</returns>
        public Dictionary<byte, IPlayerInput> GetInputsForTick(int tick)
        {
            return this[tick].ToDictionary(kvp => kvp.Key, kvp => kvp.Value.input);
        }

        public IPlayerInput PredictInput(byte playerId, int tick)
        {
            // TODO: alternate prediction algorithms

            // For now, find the last authoritative tick and just return that.
            foreach (KeyValuePair<int, Dictionary<byte, InputWrapper>> kvp in this.Reverse())
            {
                if (kvp.Key < tick && kvp.Value.TryGetValue(playerId, out InputWrapper inputWrapper) && inputWrapper.serverAuthoritative)
                {
                    return kvp.Value[playerId].input;
                }
            }

            return TypeStore.Instance.CreateBlankPlayerInput();
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

            foreach (byte playerId in playerIds)
            {
                if (!inputWrappersThisFrame.TryGetValue(playerId, out InputWrapper thisFrameInput))
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

                if (thisFrameInput.localInput == false && previousFrameInput.localInput == false)
                {
                    // This playerId isn't local, so we shouldn't send the inputs
                    continue;
                }

                if (thisFrameInput.input.Equals(previousFrameInput.input))
                {
                    // Both are the same (and could therefore be predicted), so don't include this playerId in the diff to send
                    continue;
                }

                playerInputs[playerId] = thisFrameInput.input;
            }

            return playerInputs;
        }

        #region Internal interface
        public void SetPlayerInputsAtTick(PlayerInputsDTO playerInputs, int clientTick)
        {
            foreach ((byte playerId, IPlayerInput playerInput) in playerInputs.PlayerInputs)
            {
                // If we have a locally authoritative input for this player, skip them
                if (this[clientTick].TryGetValue(playerId, out InputWrapper inputWrapper) && inputWrapper.localInput == true)
                {
                    continue;
                }

                // Set the input at clientTimeTick
                this[clientTick][playerId] = new()
                {
                    input = playerInput,
                    serverAuthoritative = true
                };
            }
        }
        #endregion Internal interface
    }
}
