//
// Copyright (c) 2010-2020 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System.Collections.Generic;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;

namespace Antmicro.Renode.Peripherals.UART
{
    public class PULP_uDMA_UART : UARTBase, IDoubleWordPeripheral, IKnownSize
    {
        public PULP_uDMA_UART(Machine machine) : base(machine)
        {
            IRQ = new GPIO();
            var registersMap = new Dictionary<long, DoubleWordRegister>
            {
                {(long)Registers.RxBaseAddress, new DoubleWordRegister(this)
                    .WithValueField(0, 32, out rxBufferAddress, name: "RX_SADDR / Rx buffer base address")
                },
                {(long)Registers.RxSize, new DoubleWordRegister(this)
                    .WithValueField(0, 17, out rxBufferSize, name: "RX_SIZE / Rx buffer size")
                },
                {(long)Registers.RxConfig, new DoubleWordRegister(this)
                    .WithFlag(4, out rxStart, name: "EN / RX channel enable and start",
                              writeCallback: (_, value) =>
                              {
                                  if(rxBufferSize.Value != 0)
                                  {
                                      var data = new byte[rxBufferSize.Value];
                                      for(var i = 0; i < rxBufferSize.Value; i++)
                                      {
                                          TryGetCharacter(out data[i]);
                                      }
                                      machine.SystemBus.WriteBytes(data, rxBufferAddress.Value, (int)rxBufferSize.Value, 0);
                                  }
                              })
                },
                {(long)Registers.TxBaseAddress, new DoubleWordRegister(this)
                    .WithValueField(0, 32, out txBufferAddress, name: "TX_SADDR / Tx buffer base address")
                },
                {(long)Registers.TxSize, new DoubleWordRegister(this)
                    .WithValueField(0, 17, out txBufferSize, name: "TX_SIZE / Tx buffer size")
                },
                {(long)Registers.TxConfig, new DoubleWordRegister(this)
                    .WithFlag(4, out txStart, name: "EN / TX channel enable and start",
                              writeCallback: (_, value) =>
                              {
                                  if(txBufferSize.Value != 0)
                                  {
                                      var data = new byte[txBufferSize.Value];
                                      machine.SystemBus.ReadBytes(txBufferAddress.Value, (int)txBufferSize.Value, data, 0);
                                      foreach(var c in data)
                                      {
                                          TransmitCharacter(c);
                                      }
                                  }
                              })
                },
                {(long)Registers.Status, new DoubleWordRegister(this)
                    .WithFlag(0, FieldMode.Read, valueProviderCallback: _ => false, name: "TX busy") // tx is never busy
                    .WithFlag(1, FieldMode.Read, valueProviderCallback: _ => false, name: "RX busy") // rx is never busy
                },
                {(long)Registers.Setup, new DoubleWordRegister(this)
                    .WithFlag(0, out parityEnable, name: "PARITY_ENA / Parity Enable")
                    .WithValueField(1, 2, out bitLength, name: "BIT_LENGTH / Character length")
                    .WithFlag(3, out stopBits, name: "STOP_BITS / Stop bits length")
                    .WithFlag(4, out pollingEnabled, name: "POLLING_EN / Polling Enabled")
                    .WithFlag(5, out cleanFIFO, name: "CLEAN_FIFO / Clean RX FIFO")
                    .WithFlag(8, out txEnabled, name: "TX_ENA / TX enabled")
                    .WithFlag(9, out rxEnabled, name: "RX_ENA / RX enabled")
                    .WithValueField(16, 16, out clockDivider, name: "CLKDIV / Clock divider")
                },
            };

            registers = new DoubleWordRegisterCollection(this, registersMap);
        }

        public override void Reset()
        {
            base.Reset();
            registers.Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            return registers.Read(offset);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            registers.Write(offset, value);
        }

        public long Size => 0x80;

        public GPIO IRQ { get; }

        public override Bits StopBits => stopBits.Value ? Bits.Two : Bits.One;
        public override Parity ParityBit => parityEnable.Value ? Parity.Even : Parity.None;

        public override uint BaudRate => 115200;

        protected override void CharWritten()
        {
            // Reception not yet supported
        }

        protected override void QueueEmptied()
        {
            // Intentionally left blank
        }

        private readonly DoubleWordRegisterCollection registers;

        private IFlagRegisterField parityEnable;
        private IFlagRegisterField pollingEnabled;
        private IFlagRegisterField cleanFIFO;
        private IFlagRegisterField txEnabled;
        private IFlagRegisterField rxEnabled;
        private IFlagRegisterField stopBits;
        private IValueRegisterField bitLength;
        private IValueRegisterField clockDivider;
        private IValueRegisterField txBufferAddress;
        private IValueRegisterField txBufferSize;
        private IFlagRegisterField txStart;
        private IValueRegisterField rxBufferAddress;
        private IValueRegisterField rxBufferSize;
        private IFlagRegisterField rxStart;

        private enum Registers : long
        {
            RxBaseAddress = 0x00,
            RxSize        = 0x04,
            RxConfig      = 0x08,
            TxBaseAddress = 0x10,
            TxSize        = 0x14,
            TxConfig      = 0x18,
            Status        = 0x20,
            Setup         = 0x24,
            Error         = 0x28,
            IrqEnable     = 0x2c,
            Valid         = 0x30,
            Data          = 0x34,
        }
    }
}
