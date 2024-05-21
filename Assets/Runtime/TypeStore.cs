using System;

namespace NSM
{
    /*
     *  This class exists to hold onto the concrete Types gathered at runtime.
     *  It's needed because we don't always control the instantiation of our
     *  structs, such as StateFrameDTO, which means that we also can't ensure
     *  that they have the types on their own.
     *
     *  Yes, this feels hacky.  PR's welcome for a less hacky version.
     */

    internal sealed class TypeStore
    {
        private Type gameStateType;

        public Type GameStateType
        {
            get { return gameStateType; }
            set
            {
                if (value.IsValueType == false)
                {
                    throw new Exception("Game State Type must represent a value type");
                }

                if (!typeof(IGameState).IsAssignableFrom(value))
                {
                    throw new Exception("Game State Type must implement NSM.IGameState");
                }

                gameStateType = value;
            }
        }

        public IGameState CreateBlankGameState()
        {
            return (IGameState)Activator.CreateInstance(GameStateType);
        }

        private Type playerInputType;

        public Type PlayerInputType
        {
            get { return playerInputType; }
            set
            {
                if (value.IsValueType == false)
                {
                    throw new Exception("Player Input Type must represent a value type");
                }

                if (!typeof(IPlayerInput).IsAssignableFrom(value))
                {
                    throw new Exception("Player Input Type must implement NSM.IPlayerInput");
                }

                playerInputType = value;
            }
        }

        public IPlayerInput CreateBlankPlayerInput()
        {
            return (IPlayerInput)Activator.CreateInstance(PlayerInputType);
        }

        private Type gameEventType;

        public Type GameEventType
        {
            get { return gameEventType; }
            set
            {
                if (value.IsValueType == false)
                {
                    throw new Exception("Game Event Type must represent a value type");
                }

                if (!typeof(IGameEvent).IsAssignableFrom(value))
                {
                    throw new Exception("Game Event Type must implement NSM.IGameEvent");
                }

                gameEventType = value;
            }
        }

        private static readonly Lazy<TypeStore> lazy = new(() => new TypeStore());

        public static TypeStore Instance
        { get { return lazy.Value; } }

        private TypeStore()
        {
        }
    }
}