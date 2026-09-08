using System;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using System.Reflection;

namespace NSM.Tests
{
    internal sealed class SimulationHarness
    {
        internal readonly IInternalNetworkStateManager Callbacks = Substitute.For<IInternalNetworkStateManager>();
        internal readonly StateBuffer States = new();
        internal readonly InputsBuffer Inputs = new();
        internal readonly GameStateManager Manager;
        internal readonly List<int> Confirmed = new();
        internal readonly List<int> UndoneEvents = new();
        internal readonly List<bool> ReplayFlags = new();
        internal byte Value;
        internal bool ThrowOnInput;

        internal SimulationHarness(int history = 8, bool authoritative = true)
        {
            TypeStore.Instance.GameStateType = typeof(TestGameStateDTO);
            TypeStore.Instance.PlayerInputType = typeof(TestPlayerInputDTO);
            TypeStore.Instance.GameEventType = typeof(TestGameEventDTO);
            var ids = Substitute.For<INetworkIdManager>();
            ids.GetAllNetworkIdGameObjects().Returns(new List<GameObject>());
            Manager = new GameStateManager(Callbacks, new GameEventsBuffer(), Inputs, States, ids,
                default, new SimulationLimits { historyTicks = history, futureInputTicks = Math.Min(history, 8) }, authoritative);
            Callbacks.When(x => x.GetGameState(ref Arg.Any<IGameState>())).Do(x => x[0] = new TestGameStateDTO { testValue = Value });
            Callbacks.When(x => x.ApplyState(Arg.Any<IGameState>())).Do(x => Value = ((TestGameStateDTO)x[0]).testValue);
            Callbacks.When(x => x.ApplyInputs(Arg.Any<Dictionary<byte, IPlayerInput>>())).Do(x =>
            {
                ReplayFlags.Add(Manager.IsReplaying);
                if (ThrowOnInput) throw new InvalidOperationException("Consumer callback failed");
                foreach (var input in ((Dictionary<byte, IPlayerInput>)x[0]).Values)
                    if (((TestPlayerInputDTO)input).buttonWasPressed) Value += 2;
            });
            Callbacks.When(x => x.PrePhysicsFrameUpdate()).Do(_ => Value++);
            Callbacks.When(x => x.ApplyEvents(Arg.Any<HashSet<IGameEvent>>())).Do(x =>
            {
                foreach (TestGameEventDTO e in (HashSet<IGameEvent>)x[0]) Value += (byte)e.EventValue;
            });
            Callbacks.When(x => x.RollbackEvents(Arg.Any<HashSet<IGameEvent>>(), Arg.Any<IGameState>())).Do(x =>
            {
                Assert.That(Value, Is.EqualTo(((TestGameStateDTO)x[1]).testValue), "Rollback must observe the exact post-frame state");
                Assert.That(Manager.IsReplaying, Is.True);
                foreach (TestGameEventDTO e in (HashSet<IGameEvent>)x[0]) UndoneEvents.Add(e.EventValue);
            });
            Callbacks.When(x => x.ConfirmFrame(Arg.Any<int>(), Arg.Any<StateFrameDTO>())).Do(x => Confirmed.Add((int)x[0]));
            Manager.SetRandomBase(42);
            Manager.CaptureInitialFrame();
        }

        internal void Advance(int frames) { for (int i = 0; i < frames; i++) Manager.RunFixedUpdate(); }
        internal static PlayerInputsDTO Press(byte player = 1) => new()
        {
            PlayerInputs = new Dictionary<byte, IPlayerInput> { [player] = new TestPlayerInputDTO { buttonWasPressed = true } }
        };
    }

    [TestFixture]
    public class HardeningTests
    {
        private SimulationMode previousMode;
        private bool previousAutoSync;
        [SetUp] public void SetUp()
        {
            previousMode = Physics.simulationMode;
            previousAutoSync = Physics.autoSyncTransforms;
            PhysicsManager.InitPhysics();
        }
        [TearDown] public void TearDown()
        {
            Physics.simulationMode = previousMode;
            Physics.autoSyncTransforms = previousAutoSync;
            TypeStore.Instance.ResetTypeStore();
        }

        private static InputAdmission Policy(int tick = 10)
        {
            var policy = new InputAdmission(new SimulationLimits());
            policy.AssignPlayer(1, 100);
            policy.AssignPlayer(2, 100);
            policy.AssignPlayer(3, 200);
            policy.ValidateValue = (_, input) => input is TestPlayerInputDTO;
            policy.BeginUpdate(tick);
            return policy;
        }
        private static InputRejection Admit(InputAdmission policy, int tick = 10, ulong sender = 100, byte player = 1) =>
            policy.TryAccept(sender, SimulationHarness.Press(player).PlayerInputs, tick, 10, 0, typeof(TestPlayerInputDTO));

        [Test] public void UnknownPlayersAndForeignSeatsAreRejected()
        {
            var policy = Policy();
            Assert.That(Admit(policy, player: 99), Is.EqualTo(InputRejection.UnknownPlayer));
            Assert.That(Admit(policy, player: 3), Is.EqualTo(InputRejection.WrongOwner));
        }
        [Test] public void MultipleSeatsForOneConnectionAreSupported()
        {
            var policy = Policy();
            var inputs = SimulationHarness.Press().PlayerInputs;
            inputs[2] = new TestPlayerInputDTO();
            Assert.That(policy.TryAccept(100, inputs, 10, 10, 0, typeof(TestPlayerInputDTO)), Is.EqualTo(InputRejection.None));
        }
        [TestCase(int.MinValue)] [TestCase(-1)] [TestCase(0)] [TestCase(19)] [TestCase(int.MaxValue)]
        public void InvalidAndOverflowingTicksAreRejected(int tick) => Assert.That(Admit(Policy(), tick), Is.EqualTo(InputRejection.InvalidTick));
        [Test] public void MissingAndThrowingValueValidatorsFailClosed()
        {
            var policy = Policy();
            policy.ValidateValue = null;
            Assert.That(Admit(policy), Is.EqualTo(InputRejection.InvalidValue));
            policy.ValidateValue = (_, _) => throw new Exception("invalid");
            Assert.That(Admit(policy), Is.EqualTo(InputRejection.InvalidValue));
        }
        [Test] public void MixedValidAndInvalidBatchDoesNotPartiallyReservePlayers()
        {
            var policy = Policy();
            var inputs = SimulationHarness.Press().PlayerInputs;
            inputs[3] = new TestPlayerInputDTO();
            Assert.That(policy.TryAccept(100, inputs, 10, 10, 0, typeof(TestPlayerInputDTO)), Is.EqualTo(InputRejection.WrongOwner));
            Assert.That(Admit(policy), Is.EqualTo(InputRejection.None));
        }
        [Test] public void DuplicateInputsRemainRejectedAcrossUpdates()
        {
            var policy = Policy();
            Assert.That(Admit(policy), Is.EqualTo(InputRejection.None));
            policy.BeginUpdate(11);
            Assert.That(Admit(policy), Is.EqualTo(InputRejection.Duplicate));
        }
        [Test] public void DisconnectRevokesEveryOwnedSeat()
        {
            var policy = Policy(); policy.RemoveClient(100);
            Assert.That(Admit(policy), Is.EqualTo(InputRejection.UnknownPlayer));
            Assert.That(Admit(policy, player: 2), Is.EqualTo(InputRejection.UnknownPlayer));
        }
        [Test] public void InvalidRequestsAlsoConsumeRateBudget()
        {
            var policy = Policy();
            for (int i = 0; i < 4; i++) Assert.That(Admit(policy, -1), Is.EqualTo(InputRejection.InvalidTick));
            Assert.That(Admit(policy), Is.EqualTo(InputRejection.RateLimited));
            policy.BeginUpdate(11);
            Assert.That(Admit(policy), Is.EqualTo(InputRejection.None));
        }
        [Test] public void HistoriesStayBoundedDuringLongSessionAndConfirmationIsOnceOnly()
        {
            var sim = new SimulationHarness(); sim.Advance(1000);
            Assert.That(sim.States.Count, Is.LessThanOrEqualTo(9));
            Assert.That(sim.Inputs.Count, Is.LessThanOrEqualTo(9));
            Assert.That(((GameEventsBuffer)sim.Manager.GameEventsBuffer).Count, Is.LessThanOrEqualTo(9));
            Assert.That(sim.Confirmed, Is.EqualTo(Enumerable.Range(1, 992).ToArray()));
            Assert.That(sim.Manager.PlayerInputsReceived(SimulationHarness.Press(), 992), Is.False);
            Assert.That(sim.Manager.PlayerInputsReceived(SimulationHarness.Press(), 993), Is.True);
            Assert.That(sim.Confirmed.Count, Is.EqualTo(992));
        }
        [Test] public void PredictionRetainsLatestInputBeforePruningBoundary()
        {
            _ = new SimulationHarness();
            var inputs = new InputsBuffer();
            inputs.SetPlayerInputsAtTick(SimulationHarness.Press(), 1);
            inputs.RemoveBefore(100);
            Assert.That(((TestPlayerInputDTO)inputs.PredictInput(1, 101)).buttonWasPressed, Is.True);
            Assert.That(inputs.Count, Is.Zero);
            Assert.That(inputs.GetInputsForTick(-500), Is.Empty);
            Assert.That(inputs.Count, Is.Zero, "Reading missing history must not grow it");
        }
        [Test] public void ReplayFlagCoversForwardSimulationAndCleansUpAfterException()
        {
            var sim = new SimulationHarness(); sim.Advance(3); sim.ReplayFlags.Clear();
            sim.Manager.PlayerInputsReceived(SimulationHarness.Press(), 2);
            Assert.That(sim.ReplayFlags, Is.All.True);
            Assert.That(sim.Manager.IsReplaying, Is.False);
            sim.ThrowOnInput = true;
            Assert.Throws<InvalidOperationException>(() => sim.Manager.PlayerInputsReceived(SimulationHarness.Press(), 2));
            Assert.That(sim.Manager.IsReplaying, Is.False);
        }
        [Test] public void RepeatedReplayUndoesTheEventsActuallyAppliedOnPreviousReplay()
        {
            var sim = new SimulationHarness();
            sim.Manager.ScheduleGameEvent(new TestGameEventDTO { EventValue = 10 }, 2);
            sim.Advance(4);
            var replacement = new GameEventsBuffer();
            replacement[2].Add(new TestGameEventDTO { EventValue = 20 });
            sim.Manager.ReplayDueToEvents(1, replacement, 3);
            Assert.That(sim.Value, Is.EqualTo(24));
            sim.Manager.ReplayDueToEvents(1, new GameEventsBuffer(), 3);
            Assert.That(sim.Value, Is.EqualTo(4));
            Assert.That(sim.UndoneEvents, Is.EqualTo(new[] { 10, 20 }));
        }
        [TestCase(0, false)] [TestCase(0, true)] [TestCase(5, false)] [TestCase(5, true)] [TestCase(10, false)] [TestCase(10, true)]
        public void SnapshotReconciliationRestoresStateAndReplaysOnlyFollowingFrames(int clientTick, bool newEvents)
        {
            var sim = new SimulationHarness(32, false); sim.Advance(clientTick);
            var events = new GameEventsBuffer();
            if (newEvents) events[6].Add(new TestGameEventDTO { EventValue = 3 });
            var snapshot = new StateFrameDTO { gameTick = 5, GameState = new TestGameStateDTO { testValue = 42 }, PhysicsState = new PhysicsStateDTO { RigidBodyStates = new() } };
            sim.Manager.SyncToServerState(snapshot, events, 5, 5, 2);
            Assert.That(sim.Manager.RealGameTick, Is.EqualTo(7));
            Assert.That(sim.Value, Is.EqualTo(newEvents ? 47 : 44));
            Assert.That(sim.Manager.LastAuthoritativeTick, Is.EqualTo(5));
        }
        [Test] public void StaleSnapshotsDoNotReplaceNewerAuthority()
        {
            var sim = new SimulationHarness(32, false);
            var snapshot = new StateFrameDTO { gameTick = 5, GameState = new TestGameStateDTO { testValue = 42 }, PhysicsState = new PhysicsStateDTO { RigidBodyStates = new() } };
            sim.Manager.SyncToServerState(snapshot, new GameEventsBuffer(), 5, 5, 0);
            sim.Manager.SyncToServerState(snapshot, new GameEventsBuffer(), 4, 5, 0);
            Assert.That(sim.Value, Is.EqualTo(42));
            Assert.That(sim.Manager.LastAuthoritativeTick, Is.EqualTo(5));
        }
        [Test] public void ClientPredictionNeverConfirmsIrreversibleEffects()
        {
            var sim = new SimulationHarness(8, false); sim.Advance(20);
            Assert.That(sim.Confirmed, Is.Empty);
            Assert.That(sim.Manager.RealGameTick, Is.EqualTo(8));
            Assert.That(sim.States.Count, Is.EqualTo(9));
        }

        [Test] public void EveryAuthoritativeRpcRequiresTheServer()
        {
            var methods = typeof(NetworkStateManager).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(method => method.Name.EndsWith("ClientRpc") && method.GetCustomAttribute<RpcAttribute>() != null).ToArray();
            Assert.That(methods.Length, Is.GreaterThanOrEqualTo(5));
            foreach (var method in methods)
                Assert.That(method.GetCustomAttribute<RpcAttribute>().InvokePermission, Is.EqualTo(RpcInvokePermission.Server), method.Name);
        }

        [Test] public void InputPacketRoundTripSupportsAll256PlayerIdsAndClearsReusedReceiver()
        {
            _ = new SimulationHarness();
            var packet = new PlayerInputsDTO();
            for (int i = 0; i < 256; i++) packet.PlayerInputs[(byte)i] = new TestPlayerInputDTO { buttonWasPressed = i % 2 == 0 };
            using var writer = new FastBufferWriter(4096, Allocator.Temp);
            writer.WriteNetworkSerializable(packet);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out PlayerInputsDTO received);
            Assert.That(received.PlayerInputs.Count, Is.EqualTo(256));
            for (int i = 0; i < 256; i++) Assert.That(((TestPlayerInputDTO)received.PlayerInputs[(byte)i]).buttonWasPressed, Is.EqualTo(i % 2 == 0));

            using var emptyWriter = new FastBufferWriter(16, Allocator.Temp);
            var empty = new PlayerInputsDTO();
            emptyWriter.WriteNetworkSerializable(empty);
            using var emptyReader = new FastBufferReader(emptyWriter, Allocator.Temp);
            emptyReader.ReadNetworkSerializableInPlace(ref received);
            Assert.That(received.PlayerInputs, Is.Empty);
        }

        [TestCase(false)] [TestCase(true)]
        public void MalformedInputPacketsRejectOversizedCountsAndDuplicateIds(bool duplicate)
        {
            _ = new SimulationHarness();
            using var writer = new FastBufferWriter(64, Allocator.Temp);
            writer.WriteValueSafe((ushort)(duplicate ? 2 : 257));
            if (duplicate)
            {
                writer.WriteValueSafe((byte)1); writer.WriteValueSafe((ushort)1); writer.WriteValueSafe(true);
                writer.WriteValueSafe((byte)1); writer.WriteValueSafe((ushort)1); writer.WriteValueSafe(false);
            }
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            Assert.Throws<InvalidOperationException>(() => reader.ReadNetworkSerializable(out PlayerInputsDTO packet));
        }

        [TestCase(1025)] [TestCase(2)]
        public void InputPayloadRejectsOversizedLengthAndTrailingBytes(int length)
        {
            _ = new SimulationHarness();
            using var writer = new FastBufferWriter(32, Allocator.Temp);
            writer.WriteValueSafe((ushort)1); writer.WriteValueSafe((byte)1);
            writer.WriteValueSafe((ushort)length); writer.WriteValueSafe(true); writer.WriteValueSafe(false);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            Assert.Throws<InvalidOperationException>(() => reader.ReadNetworkSerializable(out PlayerInputsDTO packet));
        }

        [Test] public void TruncatedInputPayloadIsRejectedBeforeAdmission()
        {
            _ = new SimulationHarness();
            using var writer = new FastBufferWriter(32, Allocator.Temp);
            writer.WriteValueSafe((ushort)1); writer.WriteValueSafe((byte)1); writer.WriteValueSafe((ushort)4);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            Assert.Throws<OverflowException>(() => reader.ReadNetworkSerializable(out PlayerInputsDTO packet));
        }

        [Test] public void QueueLimitsBoundOnePeerAndAggregateBacklog()
        {
            var obj = new GameObject("queue budget test");
            try
            {
                var nsm = obj.AddComponent<NetworkStateManager>();
                nsm.ConfigureInputPolicy(_ => { });
                var job = new RPCQueueJobSetPlayerInputsServerRpc(SimulationHarness.Press(), 1);
                for (int i = 0; i < 4; i++) Assert.That(nsm.TryEnqueue(job, 100), Is.True);
                Assert.That(nsm.TryEnqueue(job, 100), Is.False);
                for (ulong i = 0; i < 124; i++) Assert.That(nsm.TryEnqueue(job, i + 200), Is.True);
                Assert.That(nsm.TryEnqueue(job, 999), Is.False);
                Assert.That(nsm.TryEnqueue(new RPCQueueJobStartGameClientRpc(default, 1)), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); }
        }

        private static StateFrameDTO Snapshot(int tick, byte value) => new()
        {
            gameTick = tick, GameState = new TestGameStateDTO { testValue = value },
            PhysicsState = new PhysicsStateDTO { RigidBodyStates = new() }
        };

        [Test] public void FreshBaselineRecoverySkipsLostHistoryWithoutInventingConfirmedEffects()
        {
            var sim = new SimulationHarness(8, false); sim.Advance(4);
            sim.Callbacks.RestoreBaseline(Arg.Any<int>(), Arg.Any<StateFrameDTO>()).Returns(call =>
            {
                Assert.That(sim.Manager.IsReplaying, Is.True);
                Assert.That(sim.Manager.GameTick, Is.EqualTo(1000));
                sim.Value = ((TestGameStateDTO)((StateFrameDTO)call[1]).GameState).testValue;
                return true;
            });
            sim.Manager.SyncToServerState(Snapshot(1000, 42), new GameEventsBuffer(), 1000, 1000, 2);
            Assert.That(sim.Value, Is.EqualTo(44));
            Assert.That(sim.Manager.RealGameTick, Is.EqualTo(1002));
            Assert.That(sim.Manager.ConfirmedTick, Is.EqualTo(1000));
            Assert.That(sim.States.Count, Is.EqualTo(3));
            Assert.That(sim.Confirmed, Is.Empty);
            Assert.That(sim.Manager.PlayerInputsReceived(SimulationHarness.Press(), 1000), Is.False);
            Assert.That(sim.Manager.PlayerInputsReceived(SimulationHarness.Press(), 1001), Is.True);
        }

        [Test] public void MissingRecoveryHandlerFailsExplicitlyAndClearsReplayContext()
        {
            var sim = new SimulationHarness(8, false); sim.Advance(4);
            Assert.Throws<InvalidOperationException>(() => sim.Manager.SyncToServerState(Snapshot(1000, 42), new GameEventsBuffer(), 1000, 1000, 0));
            Assert.That(sim.Manager.IsReplaying, Is.False);
            Assert.That(sim.Confirmed, Is.Empty);
        }

        [Test] public void DeltaUsesLastReceivedAuthorityEvenAfterLateInputRewritesHistory()
        {
            var sim = new SimulationHarness(16, false);
            var initial = Snapshot(2, 20);
            sim.Manager.SyncToServerState(initial, new GameEventsBuffer(), 2, 2, 0);
            sim.Advance(2);
            sim.Manager.ReplayDueToInputs(SimulationHarness.Press(), 2, 4, 0);
            var delta = new StateFrameDeltaDTO(initial, Snapshot(4, 50));
            sim.Manager.ProcessStateDeltaReceived(delta, new GameEventsBuffer(), 4, 0, 2);
            Assert.That(sim.Value, Is.EqualTo(50));
        }

        [Test] public void SnapshotAtRetainedBoundaryIsIgnoredWithoutRewindingBeforeHistory()
        {
            var sim = new SimulationHarness(); sim.Advance(20);
            sim.Manager.SyncToServerState(Snapshot(12, 99), new GameEventsBuffer(), 12, 12, 0);
            Assert.That(sim.Value, Is.EqualTo(20));
        }

        [Test] public void AdmissionReplayBudgetAccountsForBothRewindAndForwardWork()
        {
            var limits = new SimulationLimits { historyTicks = 8, futureInputTicks = 8, maxReplayTicksPerUpdate = 32 };
            var policy = new InputAdmission(limits);
            policy.AssignPlayer(1, 100); policy.ValidateValue = (_, _) => true; policy.BeginUpdate(10);
            Assert.That(policy.TryAccept(100, SimulationHarness.Press().PlayerInputs, 3, 10, 2, typeof(TestPlayerInputDTO)), Is.EqualTo(InputRejection.None));
            Assert.That(policy.TryAccept(100, SimulationHarness.Press().PlayerInputs, 4, 10, 2, typeof(TestPlayerInputDTO)), Is.EqualTo(InputRejection.None));
            Assert.That(policy.TryAccept(100, SimulationHarness.Press().PlayerInputs, 5, 10, 2, typeof(TestPlayerInputDTO)), Is.EqualTo(InputRejection.ReplayBudgetExceeded));
            policy.ResetSession(); policy.BeginUpdate(10);
            Assert.That(policy.TryAccept(100, SimulationHarness.Press().PlayerInputs, 3, 10, 2, typeof(TestPlayerInputDTO)), Is.EqualTo(InputRejection.None));
        }

        [TestCase(-1)] [TestCase(65537)]
        public void EventWireCountsCannotDriveUnboundedLoops(int count)
        {
            using var writer = new FastBufferWriter(16, Allocator.Temp);
            writer.WriteValueSafe(count);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            Assert.Throws<InvalidOperationException>(() => reader.ReadNetworkSerializable(out GameEventsBuffer events));
        }

        [Test] public void ForwardedInputsAndEventsCannotExtendPredictionBeyondAuthorityWindow()
        {
            var sim = new SimulationHarness(8, false); sim.Advance(4);
            sim.Manager.ReplayDueToInputs(SimulationHarness.Press(), 3, 10, 3);
            Assert.That(sim.Manager.RealGameTick, Is.EqualTo(8));
            sim.Manager.ReplayDueToEvents(10, new GameEventsBuffer(), 3);
            Assert.That(sim.Manager.RealGameTick, Is.EqualTo(8));
            Assert.That(sim.States.Count, Is.LessThanOrEqualTo(9));
        }

        [Test] public void CorruptDeltaIsDistinguishedFromConsumerCallbackFailure()
        {
            var sim = new SimulationHarness(16, false);
            sim.Manager.SyncToServerState(Snapshot(2, 20), new GameEventsBuffer(), 2, 2, 0);
            Assert.Throws<StateDeltaDecodeException>(() => sim.Manager.ProcessStateDeltaReceived(new StateFrameDeltaDTO(), new GameEventsBuffer(), 4, 0, 2));
            Assert.That(sim.Value, Is.EqualTo(20));
            Assert.That(sim.Manager.LastAuthoritativeTick, Is.EqualTo(2));
            sim.Callbacks.When(x => x.ApplyState(Arg.Any<IGameState>())).Do(_ => throw new System.IO.InvalidDataException("Consumer restore failed"));
            var validDelta = new StateFrameDeltaDTO(Snapshot(2, 20), Snapshot(4, 40));
            Assert.Throws<System.IO.InvalidDataException>(() => sim.Manager.ProcessStateDeltaReceived(validDelta, new GameEventsBuffer(), 4, 0, 2));
        }

        [Test] public void SimulationFaultStopsLaterTicksAndReportsOnlyOnce()
        {
            var sim = new SimulationHarness(); sim.ThrowOnInput = true;
            var obj = new GameObject("fault boundary test");
            try
            {
                var nsm = obj.AddComponent<NetworkStateManager>();
                typeof(NetworkStateManager).GetField("gameStateManager", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(nsm, sim.Manager);
                typeof(NetworkStateManager).GetProperty("IsRunning").SetValue(nsm, true);
                typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(nsm, true);
                int faults = 0;
                nsm.OnSimulationFault += _ => faults++;
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("InvalidOperationException: Consumer callback failed"));
                var update = typeof(NetworkStateManager).GetMethod("FixedUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
                update.Invoke(nsm, null);
                int stoppedTick = nsm.GameTick;
                update.Invoke(nsm, null);
                Assert.That(nsm.IsRunning, Is.False);
                Assert.That(nsm.GameTick, Is.EqualTo(stoppedTick));
                Assert.That(faults, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); }
        }

        [TestCase(0.1, 0.02, 5)] [TestCase(0.1, 0.01, 10)] [TestCase(0.0, 0.02, 0)]
        public void LagCompensationUsesSimulationSecondsInsteadOfNgoTickRate(double lag, double step, int ticks)
            => Assert.That(NetworkStateManager.EstimateLagTicks(lag, step), Is.EqualTo(ticks));

        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(-1.0)]
        public void InvalidClockOffsetsAreRejected(double lag)
            => Assert.Throws<InvalidOperationException>(() => NetworkStateManager.EstimateLagTicks(lag, 0.02));
    }
}
