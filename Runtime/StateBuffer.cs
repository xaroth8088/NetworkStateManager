using System.Collections.Generic;
using UnityEngine;

namespace NSM
{
    public class StateBuffer
    {
        private readonly Dictionary<int, StateFrameDTO> stateBuffer;

        public StateBuffer()
        {
            stateBuffer = new Dictionary<int, StateFrameDTO>();
        }

        public StateFrameDTO this[int i]
        {
            get
            {
                if (i < 0)
                {
                    Debug.LogWarning("State for a negative game tick was requested to be read.");
                }

                if (!stateBuffer.ContainsKey(i))
                {
                    stateBuffer[i] = new StateFrameDTO();
                }

                return stateBuffer[i];
            }

            set
            {
                if (i < 0)
                {
                    Debug.LogWarning("State for a negative game tick was requested to be written.");
                }

                stateBuffer[i] = value;
            }
        }
    }
}