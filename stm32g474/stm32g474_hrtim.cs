//
// Copyright (c) 2010-2024 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections.Generic;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Time;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Exceptions;

namespace Antmicro.Renode.Peripherals.Timers
{
    // This class does not implement advanced-control timers interrupts
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class STM32_HRTIM : LimitTimer, IDoubleWordPeripheral, IKnownSize, INumberedGPIOOutput, IPeripheralRegister<IGPIOReceiver, NumberRegistrationPoint<int>>, IPeripheralRegister<IGPIOReceiver, NullRegistrationPoint>
    {
        public STM32_HRTIM(IMachine machine, long frequency, uint initialLimit) : base(machine.ClockSource, frequency, limit: initialLimit, direction: Direction.Ascending, enabled: false, autoUpdate: false)
        {
            IRQ = new GPIO();
            connections = Enumerable.Range(0, NumberOfCCChannels).ToDictionary(i => i, _ => (IGPIO)new GPIO());
            this.initialLimit = initialLimit;
            // If initialLimit is 0, throw an error - this is an invalid state for us, since we would not be able to infer the counter's width
            if(initialLimit == 0)
            {
                throw new ConstructionException($"{nameof(initialLimit)} has to be greater than zero");
            }
            // We need to ensure that the counter is at least as wide as the position of MSB in initialLimit
            // but since we count from 0 (log_2 (1) = 0 ) - add 1
            this.timerCounterLengthInBits = (int)Math.Floor(Math.Log(initialLimit, 2)) + 1;
            if(this.timerCounterLengthInBits > 32)
            {
                throw new ConstructionException($"Timer's width cannot be more than 32 bits - requested {this.timerCounterLengthInBits} bits (inferred from {nameof(initialLimit)})");
            }


            var registersMap = new Dictionary<long, DoubleWordRegister>
            {
                {(long)Registers.HRTIM_ICR, new DoubleWordRegister(this)
                    .WithFlag(0, name: "FLT1C")
                    .WithFlag(1, name: "FLT2C")
                    .WithFlag(2, name: "FLT3C")
                    .WithFlag(3, name: "FLT4C")
                    .WithFlag(4, name: "FLT5C")
                    .WithFlag(5, name: "SYSFLTC")
                    .WithFlag(6, name: "FLT6C")
                    .WithReservedBits(7, 9)
                    .WithFlag(16, name: "DLLRDYC")
                    .WithFlag(17, name: "BMPERC")
                    .WithReservedBits(18, 14)
                },

                {(long)Registers.HRTIM_ISR, new DoubleWordRegister(this)
                    .WithFlag(0, name: "FLT1")
                    .WithFlag(1, name: "FLT2")
                    .WithFlag(2, name: "FLT3")
                    .WithFlag(3, name: "FLT4")
                    .WithFlag(4, name: "FLT5")
                    .WithFlag(5, name: "SYSFLT")
                    .WithFlag(6, name: "FLT6")
                    .WithReservedBits(7, 9)
                    .WithFlag(16,FieldMode.Read, valueProviderCallback: _ => dllrdyie.Value, name: "DLLRDY")
                    .WithFlag(17, name: "BMPER")
                    .WithReservedBits(18, 14)
                },


                {(long)Registers.HRTIM_IER, new DoubleWordRegister(this)
                    .WithFlag(0, name: "FLT1IE")
                    .WithFlag(1, out var flt2ie, name: "FLT2IE")
                    .WithFlag(2, out var flt3ie, name: "FLT3IE")
                    .WithFlag(3, out var flt4ie, name: "FLT4IE")
                    .WithFlag(4, out var flt5ie, name: "FLT5IE")
                    .WithFlag(5, out var sysfltie, name: "SYSFLTIE")
                    .WithFlag(6, out var flt6ie, name: "FLT6IE")
                    .WithReservedBits(7, 9)
                    .WithFlag(16, FieldMode.Read, valueProviderCallback: _ => dllrdyie.Value, name: "DLLRDYIE")
                    .WithFlag(17, out var bmperie, name: "BMPERIE")
                    .WithReservedBits(18, 14)
                },


                {(long)Registers.HRTIM_DLLCR, new DoubleWordRegister(this)
                    .WithFlag(0, out dllrdyie, name: "CAL")//calibration start
                    .WithFlag(1 , name: "CALEN")
                    .WithValueField(2,2, name: "CALRTE")
                    .WithReservedBits(4, 28)
                },
            };

            registers = new DoubleWordRegisterCollection(this, registersMap);
        }
          

        public uint ReadDoubleWord(long offset)
        {
            return registers.Read(offset);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            registers.Write(offset, value);
        }

        public override void Reset()
        {
            base.Reset();
            registers.Reset();
    
        }

        public GPIO IRQ { get; private set; }
        public IReadOnlyDictionary<int, IGPIO> Connections => connections;

        public long Size => 0x400;

        public void Register(IGPIOReceiver peripheral, NumberRegistrationPoint<int> registrationPoint)
        {
            machine.RegisterAsAChildOf(this, peripheral, registrationPoint);
        }

        public void Register(IGPIOReceiver peripheral, NullRegistrationPoint registrationPoint)
        {
            machine.RegisterAsAChildOf(this, peripheral, registrationPoint);
        }

        public void Unregister(IGPIOReceiver peripheral)
        {
            machine.UnregisterAsAChildOf(this, peripheral);
        }

        private IFlagRegisterField dllrdyie;



        private bool[] ccInterruptFlag = new bool[NumberOfCCChannels];
        private bool[] ccInterruptEnable = new bool[NumberOfCCChannels];
        private bool[] ccOutputEnable = new bool[NumberOfCCChannels];
        private readonly IFlagRegisterField updateDisable;
        private readonly IFlagRegisterField updateRequestSource;
        private readonly IFlagRegisterField updateInterruptEnable;
        private readonly IFlagRegisterField autoReloadPreloadEnable;
        private readonly IValueRegisterField repetitionCounter;
        private readonly DoubleWordRegisterCollection registers;
        private readonly LimitTimer[] ccTimers = new LimitTimer[NumberOfCCChannels];
        private readonly IMachine machine;
        private readonly Dictionary<int, IGPIO> connections;

        private const int NumberOfCCChannels = 4;



        private readonly uint initialLimit;
        private readonly int timerCounterLengthInBits;
        private enum Registers : long
        {
            HRTIM_ICR = 0x38C,

            HRTIM_ISR = 0x388,

            HRTIM_IER = 0x390,

            HRTIM_DLLCR = 0x3CC

        }
    }
}

