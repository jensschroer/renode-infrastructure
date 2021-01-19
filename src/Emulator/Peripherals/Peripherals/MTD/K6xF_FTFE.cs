//
// Copyright (c) 2010-2020 Antmicro
//
//  This file is licensed under the MIT License.
//  Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections.Generic;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Memory;

namespace Antmicro.Renode.Peripherals.MTD
{
    public class K6xF_FTFE : IBytePeripheral, IWordPeripheral, IDoubleWordPeripheral, IKnownSize
    {
        public K6xF_FTFE(Machine machine, MappedMemory flash)
        {
            this.flash = flash;
            flashAddress = new byte[4];
            flashData = new byte[8];

            flashAccessError = false;
            flashProtectionViolation = false;
            lastCommandFailed = false;
            canAcceptCommands = true;

            innerLock = new object();

            ccIRQ = new GPIO();

            var registersMap = new Dictionary<long, ByteRegister>
            {
                {(long)Registers.Status, new ByteRegister(this)
                    .WithFlag(7, FieldMode.Read | FieldMode.WriteOneToClear, name: "CCIF",
                        valueProviderCallback: _ =>
                        {
                            return canAcceptCommands;
                        },
                        writeCallback: (_, value) =>
                        {
                            if(value) RunFlashCommand();
                        })
                    .WithFlag(6, FieldMode.Read | FieldMode.WriteOneToClear, name: "RDCOLERR",
                        valueProviderCallback: _ =>
                        {
                            return false;
                        })
                    .WithFlag(5, FieldMode.Read | FieldMode.WriteOneToClear, name: "ACCERR",
                        writeCallback: (_, value) =>
                        {
                            if (value) flashAccessError = false;
                        },
                        valueProviderCallback: _ =>
                        {
                            return flashAccessError;
                        })
                    .WithFlag(4, FieldMode.Read | FieldMode.WriteOneToClear, name: "FPVIOL",
                        writeCallback: (_, value) =>
                        {
                            if (value) flashProtectionViolation = false;
                        },
                        valueProviderCallback: _ =>
                        {
                            return flashProtectionViolation;
                        })                    
                    .WithReservedBits(1, 3)
                    .WithFlag(0, FieldMode.Read, name: "MGSTAT0",
                        valueProviderCallback: _ =>
                        {
                            return lastCommandFailed;
                        })
                },
                {(long)Registers.Configuration, new ByteRegister(this)
                    .WithFlag(7, out commandCompleteInterruptEnabled, name: "CCIE")
                    .WithTaggedFlag("RDCOLLIE", 6)
                    .WithTaggedFlag("ERSAREQ", 5)
                    .WithTaggedFlag("ERSSUSP", 4)
                    .WithTaggedFlag("SWAP", 3)
                    .WithTaggedFlag("PFLSH", 2)
                    .WithTaggedFlag("RAMRDY", 1)
                    .WithTaggedFlag("EEERDY", 0)
                },
                {(long)Registers.Command0, new ByteRegister(this)
                    .WithEnumField(0, 8, out flashCommand, name: "FCCOB0")
                },
                {(long)Registers.Command1, new ByteRegister(this)
                    .WithValueField(0, 8, FieldMode.Write,
                        writeCallback: (_, value) =>
                        {
                            flashAddress[2] = (byte)value;
                        }, name: "FCCOB1")
                },
                {(long)Registers.Command2, new ByteRegister(this)
                    .WithValueField(0, 8, FieldMode.Write,
                        writeCallback: (_, value) =>
                        {
                            flashAddress[1] = (byte)value;
                        }, name: "FCCOB2")
                },
                {(long)Registers.Command3, new ByteRegister(this)
                    .WithValueField(0, 8, FieldMode.Write,
                        writeCallback: (_, value) =>
                        {
                            flashAddress[0] = (byte)value;
                        }, name: "FCCOB3")
                },
                {(long)Registers.Command7, new ByteRegister(this)
                    .WithValueField(0, 8, name: "FCCOB7",
                        writeCallback: (_, value) =>
                        {
                            flashData[0] = (byte)value;
                        },
                        valueProviderCallback: _ => flashData[0])
                },
                {(long)Registers.Command6, new ByteRegister(this)
                    .WithValueField(0, 8, name: "FCCOB6",
                        writeCallback: (_, value) =>
                        {
                            flashData[1] = (byte)value;
                        },
                        valueProviderCallback: _ => flashData[1])
                },
                {(long)Registers.Command5, new ByteRegister(this)
                    .WithValueField(0, 8, name: "FCCOB5",
                        writeCallback: (_, value) =>
                        {
                            flashData[2] = (byte)value;
                        },
                        valueProviderCallback: _ => flashData[2])
                },
                {(long)Registers.Command4, new ByteRegister(this)
                    .WithValueField(0, 8, name: "FCCOB4",
                        writeCallback: (_, value) =>
                        {
                            flashData[3] = (byte)value;
                        },
                        valueProviderCallback: _ => flashData[3])
                },
                {(long)Registers.CommandB, new ByteRegister(this)
                    .WithValueField(0, 8, name: "FCCOBB",
                        writeCallback: (_, value) =>
                        {
                            flashData[4] = (byte)value;
                        },
                        valueProviderCallback: _ => flashData[4])
                },
                {(long)Registers.CommandA, new ByteRegister(this)
                    .WithValueField(0, 8, name: "FCCOBA",
                        writeCallback: (_, value) =>
                        {
                            flashData[5] = (byte)value;
                        },
                        valueProviderCallback: _ => flashData[5])
                },
                {(long)Registers.Command9, new ByteRegister(this)
                    .WithValueField(0, 8, name: "FCCOB9",
                        writeCallback: (_, value) =>
                        {
                            flashData[6] = (byte)value;
                        },
                        valueProviderCallback: _ => flashData[6])
                },
                {(long)Registers.Command8, new ByteRegister(this)
                    .WithValueField(0, 8, name: "FCCOB8",
                        writeCallback: (_, value) =>
                        {
                            flashData[7] = (byte)value;
                        },
                        valueProviderCallback: _ => flashData[7])
                }
            };

            registers = new ByteRegisterCollection(this, registersMap);

            InitFlashCommandHandlers();
        }

        public GPIO ccIRQ { get; private set; }

        public long Size => 0x1000;

        public byte ReadByte(long offset)
        {
            lock(innerLock)
            { 
                return (byte)registers.Read(offset);
            }
        }

        public void Reset()
        {
            registers.Reset();
            Array.Clear(flashData, 0, flashData.Length);
            Array.Clear(flashAddress, 0, flashAddress.Length);
        }

        public void WriteByte(long offset, byte value)
        {
            lock(innerLock)
            {
                this.Log(LogLevel.Debug, "Writing at offset 0x{0:X} value 0x{1:X}", offset, value);
                registers.Write(offset, value);
            }
        }

        public ushort ReadWord(long offset)
        {
            byte[] value = new byte[2];
            for(int i = 0; i < value.Length; ++i)
            {
                value[i] = ReadByte(offset + i);
            }
            return BitConverter.ToUInt16(value, 0);
        }

        public void WriteWord(long offset, ushort value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            for(int i = 0; i < bytes.Length; ++i)
            {
                // Internal data representation is big endian
                WriteByte(offset+i, bytes[i]);
            }
        }

        public uint ReadDoubleWord(long offset)
        {
            byte[] value = new byte[4];
            for (int i = 0; i < value.Length; ++i)
            {
                value[i] = ReadByte(offset+i);
            }
            return BitConverter.ToUInt32(value, 0);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            for(int i = 0; i < bytes.Length; ++i)
            {
                // Internal data representation is big endian
                WriteByte(offset + i, bytes[i]);
            }
        }

        private readonly ByteRegisterCollection registers;
        private readonly MappedMemory flash;
        private readonly object innerLock;

        private readonly IFlagRegisterField commandCompleteInterruptEnabled;


        private IEnumRegisterField<FlashCommands> flashCommand;
        private byte[] flashAddress;
        private byte[] flashData;

        private bool canAcceptCommands;
        private bool lastCommandFailed;
        private bool flashAccessError;
        private bool flashProtectionViolation;

        private const byte ErasePattern = 0xFF;
        private const int SectorSize = 4096;
        private readonly byte[] SectorErasePattern = Enumerable.Repeat(ErasePattern, SectorSize).ToArray();

        private Dictionary<FlashCommands, Action> flashCommandHandlers;
 
        private enum Registers
        {
            Status = 0x0000,
            Configuration = 0x0001,
            Security = 0x0002,
            Option = 0x0003,
            Command3 = 0x0004,
            Command2 = 0x0005,
            Command1 = 0x0006,
            Command0 = 0x0007,
            Command7 = 0x0008,
            Command6 = 0x0009,
            Command5 = 0x000A,
            Command4 = 0x000B,
            CommandB = 0x000C,
            CommandA = 0x000D,
            Command9 = 0x000E,
            Command8 = 0x000F,
            Protection3 = 0x0010,
            Protection2 = 0x0011,
            Protection1 = 0x0012,
            Protection0 = 0x0013,
            EEPROMProtection = 0x0016,
            DataProtection = 0x0017
        }

        private enum FlashCommands
        {
            Read1sBlock = 0x00,
            Read1sSection = 0x01,
            ProgramCheck = 0x02,
            ReadResource = 0x03,
            ProgramPhrase = 0x07,
            EraseFlashBlock = 0x08,
            EraseFlashSector = 0x09,
            ProgramSection = 0x0B,
            Read1sAllBlocks = 0x40,
            ReadOnce = 0x41,
            ProgramOnce = 0x43,
            EraseAllBlocks = 0x44,
            VerifyBackdoorAccessKey = 0x45,
            SwapControl = 0x46,
            ProgramPartition = 0x80,
            SetFlexRAMFunction = 0x81
        }

        private enum ReadMargins
        {
            Normal = 0x0,
            User = 0x1,
            Factory = 0x2
        }

        private void RunFlashCommand()
        {
            lastCommandFailed = false;
            if(flashAccessError || flashProtectionViolation)
            {
                this.Log(LogLevel.Warning, "Unable to start new flash command as the error flags are not cleard");
                return;
            }

            canAcceptCommands = false;

            if (!Enum.IsDefined(typeof(FlashCommands), flashCommand.Value))
            {
                lastCommandFailed = true;
                this.Log(LogLevel.Warning, "Unknown flash command 0x{0:X}", flashCommand.Value);
                return;
            }

            if (!flashCommandHandlers.ContainsKey(flashCommand.Value))
            {
                this.Log(LogLevel.Warning, "Flash command 0x{0:X} not implemented", flashCommand.Value);
                return;
            }

            this.Log(LogLevel.Debug, "Executing flash command {0}", Enum.GetName(typeof(FlashCommands), flashCommand.Value));
            flashCommandHandlers[flashCommand.Value]();
            if (lastCommandFailed)
            {
                this.Log(LogLevel.Warning, "Flash command {0} failed", Enum.GetName(typeof(FlashCommands), flashCommand.Value));
            }

            canAcceptCommands = true;

            if(commandCompleteInterruptEnabled.Value)
            {
                ccIRQ.Set(true);
            }
        }

        private bool ValidProgramFlashAddress(uint address, uint alignement = 0)
        {
            if ((address & alignement) != 0)
            {
                this.Log(LogLevel.Warning, "Address 0x{0:X} is not aligned to 0x{1:X}", address, alignement);
                return false;
            }
            if (address >= flash.Size)
            {
                this.Log(LogLevel.Warning, "Address 0x{0:X} is outside of the program flash space", address);
                return false;
            }
            return true;
        }

        private bool Is128bitAlignedProgramFlashAddr(uint address)
        {
            return ValidProgramFlashAddress(address, 0xF);
        }

        private bool Is64bitAlignedProgramFlashAddr(uint address)
        {
            return ValidProgramFlashAddress(address, 0x3);
        }

        private void EraseSector()
        {
            lastCommandFailed = false;

            uint targetAddress = BitConverter.ToUInt32(flashAddress, 0);
            if(!Is128bitAlignedProgramFlashAddr(targetAddress) || ((targetAddress % SectorSize) != 0))
            {
                flashAccessError = true;
                lastCommandFailed = true;
                return;
            }
            this.Log(LogLevel.Debug, "Erasing sector 0x{0:X}", targetAddress);
            flash.WriteBytes(targetAddress, SectorErasePattern, 0, SectorSize);
        }

        private void ProgramPhrase()
        {
            lastCommandFailed = false;

            uint targetAddress = BitConverter.ToUInt32(flashAddress, 0);
            if(!Is64bitAlignedProgramFlashAddr(targetAddress))
            {
                flashAccessError = true;
                lastCommandFailed = true;
                return;
            }

            this.Log(LogLevel.Debug, "Programming at address 0x{0:X}, 8 bytes", targetAddress);
            for (int i = 0; i < 8; i++)
            {
                if (flash.ReadByte((long)targetAddress+i) != ErasePattern)
                {
                    this.Log(LogLevel.Warning, "Unable to program address 0x{0:X} as it isn't erased", targetAddress + i);
                    lastCommandFailed = true;
                    return;
                }
                flash.WriteByte(targetAddress + i, flashData[i]);
            }
        }

        private void InitFlashCommandHandlers()
        {
            this.flashCommandHandlers = new Dictionary<FlashCommands, Action>
            {
                {FlashCommands.EraseFlashSector, () => EraseSector() },
                {FlashCommands.ProgramPhrase, () => ProgramPhrase() }
            };
        }
    }
}
