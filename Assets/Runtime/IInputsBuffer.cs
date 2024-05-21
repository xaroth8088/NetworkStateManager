using System.Collections.Generic;

namespace NSM
{
    internal interface IInputsBuffer
    {
        Dictionary<byte, IPlayerInput> GetInputsForTick(int tick);
        Dictionary<byte, IPlayerInput> GetMinimalInputsDiff(int tick);
        IPlayerInput PredictInput(byte playerId, int tick);
        void SetLocalInputs(Dictionary<byte, IPlayerInput> localInputs, int tick);
        void SetPlayerInputsAtTick(PlayerInputsDTO playerInputs, int clientTick);
    }
}