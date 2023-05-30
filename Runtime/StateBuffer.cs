using System.Collections.Generic;
using UnityEngine;

namespace NSM
{
    public class StateBuffer
    {
        private readonly Dictionary<int, StateFrameDTO> _stateBuffer;

        public StateBuffer()
        {
            _stateBuffer = new Dictionary<int, StateFrameDTO>();
        }

        public StateFrameDTO this[int i]
        {
            get
            {
                if (i < 0)
                {
                    Debug.LogWarning("State for a negative game tick was requested to be read.");
                }

                return _stateBuffer[i];
            }

            set
            {
                if (i < 0)
                {
                    Debug.LogWarning("State for a negative game tick was requested to be written.");
                }

                if (_stateBuffer.ContainsKey(i))
                {
                    if (_stateBuffer[i].authoritative)
                    {
                        Debug.LogError("Tried to overwrite an authoritative frame at tick " + i);
                    }
                }
                _stateBuffer[i] = value;
            }
        }
    }
}