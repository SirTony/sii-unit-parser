﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sii;

namespace Sandbox
{
    [SiiUnit("sound_engine_data")]
    class SoundEngineData
    {
        [SiiAttribute("name")]
        public string Name { get; private set; }

        [SiiAttribute("looped")]
        public bool Looped { get; private set; }

        [SiiAttribute("pitch_reference")]
        public double Pitch { get; private set; }

        [SiiAttribute("max_rpm")]
        public double MaxRPM { get; private set; }

        [SiiAttribute("min_rpm")]
        public double MinRPM { get; private set; }

        [SiiAttribute("volume")]
        public double Volume { get; private set; }

        [SiiAttribute("is_2d")]
        public bool Is2D { get; private set; }
    }
}
