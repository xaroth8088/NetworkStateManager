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
                    // Our best guess as to what this frame's data should be is what the previous frame's data was, minus any scheduled events.
                    // So, find the next earliest frame that has data, and copy it to here.

                    // TODO: keep track of the most recent frame we've received, so that we can create a blank state for any request for a frame that happens after that

                    int prevIndex;
                    for (prevIndex = i; prevIndex >= 0; prevIndex--)
                    {
                        if (stateBuffer.ContainsKey(prevIndex))
                        {
                            break;
                        }
                    }

                    stateBuffer[i] = stateBuffer[prevIndex].Duplicate();
                }

                return stateBuffer[i];
            }

            set
            {
                if (i < 0)
                {
                    Debug.LogWarning("State for a negative game tick was requested to be written.");
                }
                if( stateBuffer.ContainsKey(i))
                {
                    if(stateBuffer[i].authoritative)
                    {
                        Debug.LogError("Tried to overwrite an authoritative frame at tick " + i);
                    }
                 }
                stateBuffer[i] = value;
            }
        }
    }
}