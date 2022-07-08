# NetworkStateManager
A lightweight framework to add server-authoritative, client-predictive physics to Unity

## What does it do?
Leveraging Unity's [Netcode for GameObjects](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects), this framework provides efficient network synchronization of RigidBodies and other game state information across the network.  It includes client-side prediction, including history replaying.

## How do I use this?
### NetworkStateManager
Import `NetworkStateManager.unitypackage` to your project.  Add a new GameObject to your scene and attach the NetworkStateManager script to it.

### NetworkId's
To synchronize a GameObject that contains a RigidBody, you must add a new NetworkId component to it.  This will be in addition to Unity for Netcode's NetworkObject script.

If you're adding RigidBody's at runtime, you'll need to register them with NetworkStateManager via the `RegisterNewGameObject()` function in order for the state to be synchronized.

Note that there's a fixed pool of 255 network id's available.

### State management
* NOTE: if this piece feels awkward, I agree.  See the "How can I help this project?" section, below for more details.

** IMPORTANT **
All state stored in these `struct`s MUST be immutable for history playback and server reconciliation to be deterministic.  Make copies of your state data if needed to ensure this is the case.

There are three important `struct` partials in the `NSM` namespace that you must fully implement:

`public partial struct PlayerInputDTO : INetworkSerializable, IEquatable<PlayerInputDTO>`
This holds all information about a player's input for a given FixedUpdate frame.

`public partial struct GameStateDTO : INetworkSerializable`
This holds all information about the game's state for a given FixedUpdate frame.

`public partial struct GameEventDTO : INetworkSerializable`
This holds all information about events that happened during a given FixedUpdate frame.

Beyond implementing the specified interfaces, you can put whatever data you like into those `struct`s - just make sure that any data you want carried across the wire is fully serialized in the `NetworkSerialize<T>` interface implementation.

### Lifecycle events
To make the magic happen, this framework requires that you implement a number of event callbacks for vital parts of the process.  Each callback requires you to do a small part of your overall game logic.

** IMPORTANT **
This framework assumes your game logic happens exclusively in `FixedUpdate`.  If this is not the case (collecting user input in `Update` is the obvious unfortunate example), then it's up to you to coalesce any game state changes into things that can be represented in discrete game frames that happen at `FixedUpdate` time steps.

#### Normal gameplay
During normal frame playing, the following events will be called in this order:

`void OnGetInputs(ref Dictionary<byte, PlayerInputDTO> playerInputs)`
Fill the dictionary with (playerId, PlayerInputDTO) pairs, as appropriate.  Note that `playerId` can be any byte you like to identify the player.

`void OnApplyInputs(Dictionary<byte, PlayerInputDTO> playerInputs)`
The keys are the playerId's you set during `OnGetInputs`, above.  Take whatever input is present, and apply it to your game state / `GameObject`s as needed.

`void OnApplyEvents(List<GameEventDTO> events)`
Run through the `List` and apply the effects of each event.

`void OnPrePhysicsFrameUpdate()`
Do whatever you'd normally do with your game before the physics engine runs for the frame.

`void OnPostPhysicsFrameUpdate()`
Do whatever you'd normally do with your game after the physics engine runs for the frame.

`void OnGetGameState(ref GameStateDTO state)`
Populate the `state` variable with your game's current state.  The framework will take care of synchronizing `RigidBody` states, but any other game state that exists in your `GameObject`s or other game logic should be captured in this state.

#### History playback and server reconciliation
In order to do client-side prediction, we have to modify history and run simulations.  During this process, the flow of events is slightly different.

First, the game state is rewound by calling these events:
`void OnApplyState(GameStateDTO state)`
Read from the `state` object and set your game's state accordingly, including anything on `GameObject`s that require it.  This is the only event not described above.

`OnApplyInputs`
`OnApplyEvents`

Then, every frame that needs to be projected forwards is run via these events:
`OnApplyInputs`
`OnApplyEvents`
`OnPrePhysicsFrameUpdate`
`OnPostPhysicsFrameUpdate`
`OnGetGameState`

## How can I help this project?
It's probably obvious from looking at this code, but I'm not a Unity or C# developer by trade.  No doubt there's a lot of code in this project that isn't idiomatic Unity/C#.  PR's that help make this code more idiomatic are welcome and encouraged!

Beyond that, there are a TON of `TODO`'s scattered throughout the code.  PR's to remove those alongside new issue filings would be helpful, even if you're not going to implement the functionality yourself.

Also, a simple reference project that demonstrates proper usage of this library would be awesome.  While I _am_ using this framework in a private project of my own, having someone else create this demo serves another purpose: it'll help ferret out issues in the code and documentation that I, as the creator of the framework, simply wouldn't encounter.

### `partial struct` madness

My non-idiomatic Unity/C# code shows _especially_ true for the `partial struct` pattern for game state, so it's probably worthwhile to explain what I'm trying to accomplish there and the alternatives I've tried.

#### Constraints
* Unity's RPC framework requires that the three parts of state data be non-nullable objects that implement `INetworkSerializable`.  This means no `class`'s to represent this state.
  * This is probably net positive overall, because it'd be nice for the state objects to be immutable.
* I don't want people who use this framework to have to manage the lifecycle of `NetworkStateManager` manually.
  * My understanding is that this constraint prohibits solutions that turn it into `NetworkStateManager<T> where T : INetworkSerializable, new()`.
  * I'm open to changing this if this sort of thing is more idiomatic to Unity than it looks.  As it stands, it seems the more "correct" thing to do is let it be part of a `GameObject` in a prefab, hence this constraint.
* I don't want implementers of the lifecycle events to have to do runtime type checking and coercion on their inputs.
  * That is to say, a delegate of `void OnApplyGameState(object state)` would appear to make this framework trickier to use.
  * Similar to the above, if this is actually the more idiomatic way to do this in Unity/C#, then I'm open to making that change.

#### What I've tried (and why they don't work)
* Wouldn't it be nice if the events could be generic like `void OnApplyState<T>(T state) where T : INetworkStateManagerGameStateDTO, new()`?
  * Alas, while I can make the `delegate` into a generic, the `T` needs to be declared at the `class` level to make this work, which bumps into that second constraint, above.
* How about just creating `INetworkStateManagerGameStateDTO` and let everything take that as a param?
  * Nope - C# won't let you upcast `INetworkStateManagerGameStateDTO` to your implementing struct because its type system can't be sure that the one you're getting is the one you're trying to use it as.

Any and all assistance - including just saying "turns out that's actually the best way to do what you want" - would be greatly appreciated.
