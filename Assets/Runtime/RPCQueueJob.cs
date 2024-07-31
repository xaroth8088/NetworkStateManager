using Unity.Netcode;

namespace NSM
{
    internal class RPCQueueJob
    {
    }

    internal class RPCQueueJobForwardPlayerInputsClientRpc : RPCQueueJob
    {
        public RPCQueueJobForwardPlayerInputsClientRpc(PlayerInputsDTO playerInputs, int clientTimeTick, int serverTick)
        {
            this.playerInputs = playerInputs;
            this.clientTimeTick = clientTimeTick;
            this.serverTick = serverTick;
        }

        public int clientTimeTick { get; private set; }
        public PlayerInputsDTO playerInputs { get; private set; }
        public int serverTick { get; private set; }
    }

    internal class RPCQueueJobProcessFullStateUpdateClientRpc : RPCQueueJob
    {
        public RPCQueueJobProcessFullStateUpdateClientRpc(StateFrameDTO serverGameState, GameEventsBuffer serverGameEventsBuffer, int frameTick, int serverNow)
        {
            this.serverGameState = serverGameState;
            this.serverGameEventsBuffer = serverGameEventsBuffer;
            this.frameTick = frameTick;
            this.serverNow = serverNow;
        }

        public int frameTick { get; private set; }
        public GameEventsBuffer serverGameEventsBuffer { get; private set; }
        public StateFrameDTO serverGameState { get; private set; }
        public int serverNow { get; private set; }
    }

    internal class RPCQueueJobProcessStateDeltaUpdateClientRpc : RPCQueueJob
    {
        public RPCQueueJobProcessStateDeltaUpdateClientRpc(StateFrameDeltaDTO serverGameStateDelta, GameEventsBuffer newGameEventsBuffer, int serverTick)
        {
            this.serverGameStateDelta = serverGameStateDelta;
            this.newGameEventsBuffer = newGameEventsBuffer;
            this.serverTick = serverTick;
        }

        public GameEventsBuffer newGameEventsBuffer { get; private set; }
        public StateFrameDeltaDTO serverGameStateDelta { get; private set; }
        public int serverTick { get; private set; }
    }

    internal class RPCQueueJobRequestFullStateUpdateServerRpc : RPCQueueJob
    {
        public RPCQueueJobRequestFullStateUpdateServerRpc(RpcParams rpcParams)
        { this.rpcParams = rpcParams; }

        public RpcParams rpcParams { get; private set; }
    }

    internal class RPCQueueJobSetPlayerInputsServerRpc : RPCQueueJob
    {
        public RPCQueueJobSetPlayerInputsServerRpc(PlayerInputsDTO playerInputs, int clientTimeTick, RpcParams rpcParams = default)
        {
            this.playerInputs = playerInputs;
            this.clientTimeTick = clientTimeTick;
            this.rpcParams = rpcParams;
        }

        public int clientTimeTick { get; private set; }
        public PlayerInputsDTO playerInputs { get; private set; }
        public RpcParams rpcParams { get; private set; }
    }

    internal class RPCQueueJobStartGameClientRpc : RPCQueueJob
    {
        public RPCQueueJobStartGameClientRpc(StateFrameDTO initialStateFrame, int randomSeedBase)
        {
            this.initialStateFrame = initialStateFrame;
            this.randomSeedBase = randomSeedBase;
        }

        public StateFrameDTO initialStateFrame { get; private set; }
        public int randomSeedBase { get; private set; }
    }

    internal class RPCQueueJobSyncGameEventsToClientsClientRpc : RPCQueueJob
    {
        public RPCQueueJobSyncGameEventsToClientsClientRpc(int serverTimeTick, GameEventsBuffer newGameEventsBuffer)
        {
            this.serverTimeTick = serverTimeTick;
            this.newGameEventsBuffer = newGameEventsBuffer;
        }

        public GameEventsBuffer newGameEventsBuffer { get; private set; }
        public int serverTimeTick { get; private set; }
    }
}