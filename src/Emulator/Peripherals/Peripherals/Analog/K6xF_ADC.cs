//
// Copyright (c) 2010-2021 Antmicro
//
//  This file is licensed under the MIT License.
//  Full license text is available in 'licenses/MIT.txt'.
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;

namespace Antmicro.Renode.Peripherals.Analog
{
    public class K6xF_ADC : BasicDoubleWordPeripheral, IKnownSize
    {
        public K6xF_ADC(Machine machine) : base(machine)
        {

            stateA = State.Idle;
            stateB = State.Idle;

            Registers.StatusControl1A.Define(this)
                .WithReservedBits(8, 24)
                .WithFlag(7, FieldMode.Read, name: "COCO", valueProviderCallback: _ =>
                {
                    switch(stateA)
                    {
                        case State.Idle:
                            return true;

                        case State.ConversionStarted:
                            stateA = State.SampleReady;
                            return false;

                        case State.SampleReady:
                            stateA = State.Idle;
                            return true;

                        default:
                            throw new ArgumentException($"Unexpected stateA: {stateA}");
                    }
                })
                .WithTaggedFlag("AIEN",6)
                .WithTaggedFlag("DIFF", 5)
                .WithValueField(0, 5, name: "ADCH")
            ;

            Registers.StatusControl1B.Define(this)
                .WithReservedBits(8, 24)
                .WithFlag(7, FieldMode.Read, name: "COCO", valueProviderCallback: _ =>
                {
                    switch(stateB)
                    {
                        case State.Idle:
                            return true;

                        case State.ConversionStarted:
                            stateB = State.SampleReady;
                            return false;

                        case State.SampleReady:
                            stateB = State.Idle;
                            return true;

                        default:
                            throw new ArgumentException($"Unexpected state: {stateB}");
                    }
                })
                .WithTaggedFlag("AIEN",6)
                .WithTaggedFlag("DIFF", 5)
                .WithValueField(0, 5, name: "ADCH")
            ;

            Registers.Configuration1.Define(this)
                .WithReservedBits(8, 24)
                .WithTaggedFlag("ADLPC", 7)
                .WithValueField(5, 2, name: "ADIV")
                .WithFlag(4, name: "ADLSMP")
                .WithValueField(2, 2, name: "MODE")
                .WithValueField(0, 2, name: "ADICLK")
            ;

            Registers.StatusControl3.Define(this)
                .WithReservedBits(8, 24)
                .WithTaggedFlag("CAL", 7)
                .WithTaggedFlag("CALF", 6)
                .WithReservedBits(4, 2)
                .WithTaggedFlag("ADCO", 3)
                .WithTaggedFlag("AVGE", 2)
                .WithValueField(0, 2, name: "AVGS")
            ;
        }

        public long Size => 0x1000;

        private State stateA;
        private State stateB;

        private enum Registers 
        {
            StatusControl1A = 0x00, //(ADC0_SC1A)
            StatusControl1B = 0x04, //(ADC0_SC1B)
            Configuration1 = 0x08, //(ADC0_CFG1)
            Configuration2 = 0x0C, //(ADC0_CFG2)
            DataResultA = 0x10, //(ADC0_RA)
            DataResultB = 0x14, //(ADC0_RB)
            CompareValue1 = 0x18, //(ADC0_CV1)
            CompareValue2 = 0x1C, //(ADC0_CV2)
            StatusControl2 = 0x20, //(ADC0_SC2)
            StatusControl3 = 0x24, //(ADC0_SC3)
            OffsetCorrection = 0x28, //(ADC0_OFS)
            PlusGain = 0x2C, //(ADC0_PG)
            MinusGain = 0x30, //(ADC0_MG)
            PlusCalibrationD = 0x34, //(ADC0_CLPD)
            PlusCalibrationS = 0x38, //(ADC0_CLPS)
            PlusCalibration4 = 0x3C, //(ADC0_CLP4)
            PlusCalibration3 = 0x40, //(ADC0_CLP3)
            PlusCalibration2 = 0x44, //(ADC0_CLP2)
            PlusCalibration1 = 0x48, //(ADC0_CLP1)
            PlusCalibration0 = 0x4C, //(ADC0_CLP0)
            MinusCalibrationD = 0x54, //(ADC0_CLMD)
            MinusCalibrationS = 0x58, //(ADC0_CLMS)
            MinusCalibration4 = 0x5C, //(ADC0_CLM4)
            MinusCalibration3 = 0x60, //(ADC0_CLM3)
            MinusCalibration2 = 0x64, //(ADC0_CLM2)
            MinusCalibration1 = 0x68, //(ADC0_CLM1)
            MinusCalibration0 = 0x6C //(ADC0_CLM0)
        }

        private enum State
        {
            Idle,
            ConversionStarted,
            SampleReady
        }
    }
}
