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

                if (_stateBuffer.ContainsKey(i) && _stateBuffer[i].authoritative)
                {
                    Debug.LogWarning($"Tried to overwrite an authoritative frame at tick {i}.  This can happen in certain edge-cases, and is probably not a cause for concern.");
                    // Specifically, the edge-case in question looks like this:
                    //      Server sends out its normal delta update
                    //      Server receives input from some client that's timestamped from before the delta update
                    //      Server rewinds and replays, then sends the input to all clients
                    //      Due to network conditions, the delta and input update both arrive _in the same frame_ on the client that's showing this error
                    //      The client runs the delta update, marking the frame as authoritative
                    //      The client runs the input update (again, timestamped from before the delta's timestamp), and wants to update the authoritative frame
                    //      This warning condition is triggered

                    // TODO: there's gotta be a better way to handle this
                }
                _stateBuffer[i] = value;
            }
        }
    }
}