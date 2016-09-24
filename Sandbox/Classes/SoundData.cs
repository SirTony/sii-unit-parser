using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sii;

namespace Sandbox
{
    [SiiUnit("sound_data")]
    class SoundData
    {
        [SiiAttribute("name")]
        public string Name { get; private set; }

        [SiiAttribute("looped")]
        public bool Looped { get; private set; }

        [SiiAttribute("volume")]
        public double Volume { get; private set; }
    }
}
