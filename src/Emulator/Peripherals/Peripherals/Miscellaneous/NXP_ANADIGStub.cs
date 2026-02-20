//
// Copyright (c) 2010-2025 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // Stub peripheral for NXP ANADIG_PLL / OSC register blocks.
    //
    // Stores written values and auto-sets hardware status bits to simulate
    // PLL lock, PFD stabilization, and oscillator readiness.
    //
    // PFD stable bits are deferred: when a PFD is ungated (per-byte bit 7
    // transitions 1->0), the stable bit (bit 6) is set in the stored value
    // on the NEXT read, not immediately.  This ensures firmware polling
    // loops that check for a change in the stable bit can detect the
    // transition even when re-configuring an already-stable PFD.
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord)]
    public class NXP_ANADIGStub : IDoubleWordPeripheral, IKnownSize
    {
        public NXP_ANADIGStub(long size, uint readOrMask = 0x20000000, bool enableAutoStable = true)
        {
            this.size = size;
            this.readOrMask = readOrMask;
            this.enableAutoStable = enableAutoStable;
            storage = new uint[size / 4];
            pendingStable = new uint[size / 4];
        }

        public uint ReadDoubleWord(long offset)
        {
            var idx = offset / 4;
            // Return current stored value, then apply pending stable bits
            // so that the NEXT read will include them.  This one-read delay
            // allows firmware to observe the brief stable=0 state after
            // ungating, matching real hardware behavior.
            var result = storage[idx] | readOrMask;
            if(enableAutoStable)
            {
                storage[idx] |= pendingStable[idx];
                pendingStable[idx] = 0;
            }
            return result;
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            var idx = offset / 4;
            var oldValue = storage[idx];
            storage[idx] = value;

            if(!enableAutoStable)
            {
                return;
            }

            for(var i = 0; i < 4; i++)
            {
                var shift = i * 8;
                var oldGate = (oldValue >> (shift + 7)) & 1;
                var newGate = (value >> (shift + 7)) & 1;

                if(oldGate == 0 && newGate == 1)
                {
                    // Gating (bit 7: 0->1): clear stable bit immediately.
                    storage[idx] &= ~(uint)(0x40 << shift);
                }
                else if(oldGate == 1 && newGate == 0)
                {
                    // Ungating (bit 7: 1->0): defer stable bit to next read.
                    pendingStable[idx] |= (uint)(0x40 << shift);
                }
            }

            // PLL gate->stable: bit 30 (gate) 1->0 sets bit 29 (stable).
            // Deferred to next read for consistency.
            if((oldValue & Bit30) != 0 && (value & Bit30) == 0)
            {
                pendingStable[idx] |= Bit29;
            }

            // Gating at register level: bit 30 (gate) 0->1 clears bit 29.
            if((oldValue & Bit30) == 0 && (value & Bit30) != 0)
            {
                storage[idx] &= ~Bit29;
            }

            // OSC region: auto-set bit 30 when enable bits are written.
            if(offset >= OscRegionOffset
                && ((oldValue ^ value) & Bit30) == 0
                && value != oldValue)
            {
                storage[idx] |= Bit30;
            }
        }

        public void Reset()
        {
            for(var i = 0; i < storage.Length; i++)
            {
                storage[i] = 0;
                pendingStable[i] = 0;
            }
        }

        public long Size => size;

        private const uint Bit29 = 0x20000000;
        private const uint Bit30 = 0x40000000;
        private const long OscRegionOffset = 0x300;

        private readonly long size;
        private readonly uint readOrMask;
        private readonly bool enableAutoStable;
        private readonly uint[] storage;
        private readonly uint[] pendingStable;
    }
}
