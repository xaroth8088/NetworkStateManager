using System;
using System.Collections.Generic;

namespace NSM
{
    public interface INetworkStateManager
    {
        int GameTick { get; }
        bool isReplaying { get; }
        NetworkIdManager NetworkIdManager { get; }
        RandomManager Random { get; }

        event NetworkStateManager.ApplyEventsDelegateHandler OnApplyEvents;
        event NetworkStateManager.ApplyInputsDelegateHandler OnApplyInputs;
        event NetworkStateManager.ApplyStateDelegateHandler OnApplyState;
        event NetworkStateManager.OnGetGameStateDelegateHandler OnGetGameState;
        event NetworkStateManager.OnGetInputsDelegateHandler OnGetInputs;
        event NetworkStateManager.OnPostPhysicsFrameUpdateDelegateHandler OnPostPhysicsFrameUpdate;
        event NetworkStateManager.OnPrePhysicsFrameUpdateDelegateHandler OnPrePhysicsFrameUpdate;
        event NetworkStateManager.RollbackEventsDelegateHandler OnRollbackEvents;

        IPlayerInput PredictInputForPlayer(byte playerId);
        void RemoveEventAtTick(int eventTick, Predicate<IGameEvent> gameEventPredicate);
        void ScheduleGameEvent(IGameEvent gameEvent, int eventTick = -1);
        void StartNetworkStateManager(Type gameStateType, Type playerInputType, Type gameEventType);
        void VerboseLog(string message);
    }

    internal interface IInternalNetworkStateManager : INetworkStateManager
    {
        void ApplyEvents(HashSet<IGameEvent> events);
        void RollbackEvents(HashSet<IGameEvent> events, IGameState stateAfterEvent);
        void ApplyInputs(Dictionary<byte, IPlayerInput> playerInputs);
        void ApplyState(IGameState gameState);
        void GetGameState(ref IGameState gameState);
        void GetInputs(ref Dictionary<byte, IPlayerInput> inputs);
        void PostPhysicsFrameUpdate();
        void PrePhysicsFrameUpdate();
    }
}