# NetworkStateManager

A lightweight framework to add server-authoritative, client-predictive physics to Unity

## What does it do?

Leveraging Unity's [Netcode for GameObjects](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects), this framework provides efficient network synchronization of RigidBodies and other game state information across the network.  It includes client-side prediction, including history replaying.

## How do I use this?

### NetworkStateManager

1. Import `NetworkStateManager.unitypackage` to your project.
2. Add a new `GameObject` to your scene and attach the `NetworkStateManager` script to it.
3. When your scene is fully loaded, call `StartNetworkManager()` with the runtime-determined types of your game state objects (see "State management", below), like so:

```C#
// Grab the NetworkStateManager instance
NetworkStateManager networkStateManager = FindObjectOfType<NetworkStateManager>();

// Attach event handlers for lifecycle events
networkStateManager.OnGetGameState += NetworkStateManager_OnGetGameState;
networkStateManager.OnPostPhysicsFrameUpdate += NetworkStateManager_OnPostPhysicsFrameUpdate;
networkStateManager.OnApplyState += NetworkStateManager_OnApplyState;
networkStateManager.OnApplyInputs += NetworkStateManager_OnApplyInputs;
networkStateManager.OnApplyEvents += NetworkStateManager_OnApplyEvents;

// Tell NetworkStateManager that it's good to start
networkStateManager.StartNetworkStateManager(typeof(MyGameStateObject), typeof(MyPlayerInputObject), typeof(MyGameEventObject));
```

### NetworkId

To synchronize a `GameObject` that contains a `RigidBody`, you must add a `NetworkId` component to it.  This will be in addition to Unity for Netcode's `NetworkObject` script.

If you're adding `RigidBody`s at runtime, you'll need to register them with `NetworkStateManager` via the `RegisterNewGameObject()` function in order for the state to be synchronized.

Note that there's a fixed pool of 255 network ids available, so trying to synchronize more than 255 physics-based game objects is unsupported.  (And I guarantee your players won't have the network bandwidth required to support that many objects anyway!)

### State management

**IMPORTANT**
All state stored in these `struct`s MUST be immutable for history playback and server reconciliation to be deterministic.  Make copies of your state data if needed to ensure this is the case.

There are three types of game objects that the framework needs to know about in order to do its magic.  You can define these types any way you like, provided that they:

1. are `struct`s, and
2. they implement the appropriate interface

All three interfaces derive from at least `INetworkSerializable`, from Unity's Netcode for GameObjects library.  ([Unity's documentation here](https://docs-multiplayer.unity3d.com/netcode/current/advanced-topics/serialization/inetworkserializable/index.html))

These game objects will be synchronized across the network automagically, and will be handed back to your game logic via the appropriate lifecycle events.

#### Game State (`IGameState`)

This object should hold general data about the game, such as scores, player health values, etc.  Basically, if you need all your clients to be in sync on a game value, this is the object you'll put it in.

#### Player Input (`IPlayerInput`)

This object should hold input information from the player.  Typical examples of input data might include things like x/y axis values from a gamepad's analog sticks, booleans to indicate that a player pressed a specific button, etc.

#### Game Event (`IGameEvent`)

This object should hold information about an event happening in the game.  This should only be used for events that need to be synchronized in time across clients.  Notably, these can be scheduled for a future game tick.

### Lifecycle events

To make the magic happen, this framework requires that you implement a number of event callbacks for vital parts of the process.  Each callback requires you to do a small part of your overall game logic.

**IMPORTANT**
This framework assumes your game logic happens exclusively in `FixedUpdate`.  If this is not the case (collecting user input in `Update` is the obvious unfortunate example), then it's up to you to coalesce any game state changes into things that can be represented in discrete game frames that happen at `FixedUpdate` time steps.

#### Normal gameplay

During normal frame playing, the following events will be called in this order:

`void OnGetInputs(ref Dictionary<byte, IPlayerInput> playerInputs)`
Fill the dictionary with `(playerId, <your IPlayerInput object here>)` pairs, as appropriate.  Note that `playerId` can be any byte you like to identify the player.

Because the `playerId` is set by you, you can even have several players hosted by the same client - allowing both network players and couch co-op to play nicely together!

`void OnApplyInputs(Dictionary<byte, IPlayerInput> playerInputs)`
The keys are the `playerId`s you set during `OnGetInputs`, above.  Take whatever input is present, and apply it to your game state / `GameObject`s as needed.

You'll probably want to cast `playerInputs` back to your own game state object's type before using it.

`void OnApplyEvents(List<IGameEvent> events)`
Run through the `List` and apply the effects of each event.  Similar to player input, above, you'll probably want to cast the objects inside of `events` appropriately.

`void OnPrePhysicsFrameUpdate()`
Do whatever you'd normally do with your game before the physics engine runs for the frame.

`void OnPostPhysicsFrameUpdate()`
Do whatever you'd normally do with your game after the physics engine runs for the frame.

`void OnGetGameState(ref IGameState state)`
Populate the `state` variable with your game's current state.  The framework will take care of synchronizing `RigidBody` states, but any other game state that exists in your `GameObject`s or other game logic should be captured in this state.

#### History playback and server reconciliation

In order to do client-side prediction, we have to modify history and run simulations.  During this process, the flow of events is slightly different.

First, the game state is rewound by calling these events:
`void OnApplyState(IGameState state)`
Read from the `state` object (after casting to your custom game state object type) and set your game's state accordingly, including anything on `GameObject`s that require it.  This is the only event not described above.

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

Also, a simple reference project or three that demonstrate proper usage of this library would be awesome to put into `Samples/`.  While I _am_ using this framework in a private project of my own, having someone else create this demo serves another important purpose: it'll help ferret out issues in the code and documentation that I, as the creator of the framework, simply wouldn't encounter.

### `typeof(<your game state object here>)` weirdness when starting up `NetworkStateManager`

My non-idiomatic Unity/C# code shows _especially_ true for the use of reflection in handling game state, so it's probably worthwhile to explain how I landed here.

#### Why are the runtime types needed?

The short version is that I need a way to instantiate your custom type as part of the serialization process, in order to get your data into your custom objects.

Verifying that your objects implement the various interfaces happens via reflection at runtime, mostly because I couldn't figure out a way to do that statically at compile-time.

#### Constraints

* Unity's RPC framework requires that the state data be represented by value-type objects that implement `INetworkSerializable`.
  * More deeply, this is because their serialization functionality needs a concrete instance that it can copy data into, and it doesn't want to know / care about any constructor complexity.  I suspect that they weren't able to come up with a cleaner way to do their RPC wrappers that include non-basic data types as arguments, because using a `class` here wouldn't work for this use-case.
  * This is probably net positive overall, because it's definitely nice for the state objects to be immutable.
* I strongly prefer that people who use this framework be able to just drop the script onto a `GameObject`, rather than having to manually instantiate `NetworkStateManager`.
  * My understanding is that this constraint prohibits solutions that turn it into `NetworkStateManager<T> where T : INetworkSerializable, new()`.
  * I'm open to changing this if this sort of thing is more idiomatic to Unity than it looks.  As it stands, it seems the more "correct" thing to do is let it be part of a `GameObject` in a prefab, hence the constraint of passing the types in at runtime.
* I don't want implementers of the lifecycle events to have to do runtime type checking and coercion on their inputs.
  * That is to say, a delegate of `void OnApplyGameState(object state)` would appear to make this framework trickier to use.
  * Casting back to one's own game objects whenever the `Apply*()` events occur is ok-ish, _I guess_, but it'd sure be nice to find a way where this isn't required.
  * Similar to the above, if this is actually the more idiomatic way to do this in Unity/C#, then I'm open to making that change.

#### What I've tried (and why they don't work)

* Wouldn't it be nice if the events could be generic like `void OnApplyState<T>(T state) where T : INetworkStateManagerGameStateDTO, new()`?
  * Alas, while I can make the `delegate` into a generic, the `T` needs to be declared at the `class` level to make this work, which bumps into that second constraint, above.
* How about just creating `INetworkStateManagerGameStateDTO` and let everything take that as a param?
  * Nope - C# won't let you upcast `INetworkStateManagerGameStateDTO` to your implementing struct because its type system can't be sure that the one you're getting is the one you're trying to use it as.

Any and all assistance - including just saying "turns out that's actually the best way to do what you want" - would be greatly appreciated.
