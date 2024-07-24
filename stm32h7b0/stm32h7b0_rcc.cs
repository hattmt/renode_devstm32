using System;
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Timers;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public sealed class STM32H7B0_RCC : IDoubleWordPeripheral, IKnownSize, IProvidesRegisterCollection<DoubleWordRegisterCollection>
    {
        public STM32H7B0_RCC(IMachine machine, bool rtcPeripheral)
        {
            var registersMap = new Dictionary<long, DoubleWordRegister>
            {
                {(long)Registers.ClockControl, new DoubleWordRegister(this,0x1)
                    .WithFlag(0, out var hsion, name: "HSION")
                    .WithFlag(1, out var HSIKERON, name: "HSIKERON")
                    .WithFlag(2, FieldMode.Read, valueProviderCallback: _ => hsion.Value, name: "HSIRDY")
                    .WithValueField(3, 2, name: "HSIDIV")
                    .WithFlag(5, name: "HSIDIVF")
                    .WithReservedBits(6, 1)
                    .WithFlag(7, out var csion, name: "CSION" )
                    .WithFlag(8, FieldMode.Read, valueProviderCallback: _ => csion.Value, name: "CSIRDY")
                    .WithFlag(9,out var CSIKERON, name: "CSIKERON")
                    .WithReservedBits(10, 2)
                    .WithFlag(12, out var HSI48ON, name: "HSI48ON")
                    .WithFlag(13,FieldMode.Read, valueProviderCallback: _ => HSI48ON.Value, name: "HSI48RDY")
                    .WithFlag(14, name: "CPUCKRDY")
                    .WithFlag(15, name: "CDCKRDY")
                    .WithFlag(16, out var hseon, name: "HSEON")
                    .WithFlag(17, FieldMode.Read, valueProviderCallback: _ => hseon.Value, name: "HSERDY")
                    .WithFlag(18, name:"HSEBYP")
                    .WithFlag(19, name:"HSECSSON" )
                    .WithFlag(20, name:"HSEEXT" )
                    .WithReservedBits(21, 3)
                    .WithFlag(24, out var pll1on, name: "PLL1ON")
                    .WithFlag(25, FieldMode.Read, valueProviderCallback: _ => pll1on.Value, name: "PLL1RDY")
                    .WithFlag(26, out var pll2on, name: "PLL2ON")
                    .WithFlag(27, FieldMode.Read, valueProviderCallback: _ => pll2on.Value, name: "PLL2RDY")
                    .WithFlag(28, out var pll3on, name: "PLL3ON")
                    .WithFlag(29, FieldMode.Read, valueProviderCallback: _ => pll3on.Value, name: "PLL3RDY")
                    .WithReservedBits(30, 2)
                },
                {(long)Registers.PLLConfiguration, new DoubleWordRegister(this)
                    .WithValueField(0, 2, name: "PLLSRC")
                    .WithReservedBits(2, 2)
                    .WithValueField(4, 3, name: "PLLM")
                    .WithReservedBits(7, 1)
                    .WithValueField(8, 7, name: "PLLN")
                    .WithReservedBits(15, 1)
                    .WithValueField(16, 1, name: "PLLPEN")
                    .WithValueField(17, 5, name: "PLLP")
                    .WithReservedBits(22, 2)
                    .WithValueField(24, 1, name: "PLLQEN")
                    .WithValueField(25, 3, name: "PLLQ")
                    .WithValueField(28, 1, name: "PLLR")
                    .WithValueField(29, 3, name: "PLLR")
                },
                {(long)Registers.ClockConfiguration, new DoubleWordRegister(this)
                    .WithValueField(0, 3, out var systemClockSwitch, name: "SW")
                    .WithValueField(3, 3, FieldMode.Read, name: "SWS", valueProviderCallback: _ => systemClockSwitch.Value)
                    .WithValueField(6, 1, out var STOPWUCK, name: "STOPWUCK")
                    .WithValueField(7, 1, out var STOPKERWUCK, name: "STOPKERWUCK")
                    .WithValueField(8, 6, out var RTCPRE, name: "RTCPRE")
                    .WithReservedBits( 14, 1 )
                    .WithValueField(15,1, out var TIMPRE ,name:"TIMPRE")
                    .WithReservedBits( 16, 2 )
                    .WithValueField(18, 4, out var MCO1PRE, name: "MCO1PRE")
                    .WithValueField(22, 3, out var MCO1SEL, name: "MCO1SEL")
                    .WithValueField(25, 4, out var mco2pre, name: "MCO2PRE")
                    .WithValueField(29, 3, out var MCO2SEL, name: "MCO2SEL")
                },
                {(long)Registers.DomainClockConfigurationR1, new DoubleWordRegister(this)
                    .WithValueField(0, 4, name: "HPRE")
                    .WithValueField(4, 3, name: "CDPPRE")
                    .WithReservedBits( 7, 1 )
                    .WithValueField( 8, 4, out var CDCPRE ,name:"CDCPRE")
                    .WithReservedBits( 12, 20 )
                },
                {(long)Registers.DomainClockConfigurationR2, new DoubleWordRegister(this)
                    .WithReservedBits( 0, 4 )
                    .WithValueField(4, 3, name: "CDPPRE1")
                    .WithReservedBits( 7, 1 )
                    .WithValueField(8, 3, name: "CDPPRE2")
                    .WithReservedBits( 11, 21 )
                },
                {(long)Registers.PllClkSrcSelectionR, new DoubleWordRegister(this)
                    .WithValueField(0, 2, name: "PLLSRC")
                    .WithReservedBits( 2, 1 )
                    .WithValueField(4, 6, name: "DIVM1")
                    .WithReservedBits( 10, 2 )
                    .WithValueField(12, 6, name: "DIVM2")
                    .WithReservedBits( 18, 2 )
                    .WithValueField(20, 6, name: "DIVM3")
                    .WithReservedBits( 26, 6 )
                },
                {(long)Registers.PllConfigR, new DoubleWordRegister(this)//PLLCFGR
                    .WithValueField(0, 1, name: "PLL1FRACEN")
                    .WithValueField(1, 1, name: "PLL1VCOSEL")
                    .WithValueField(2, 2, name: "PLL1RGE")
                    .WithValueField(4, 1, name: "PLL2FRACEN")
                    .WithValueField(5, 1, name: "PLL2FRACEN")
                    .WithValueField(6, 2, name: "PLL2RGE")
                    .WithValueField(8, 1, name: "PLL3FRACEN")
                    .WithValueField(9, 1, name: "PLL3VCOSEL")
                    .WithValueField(10, 2, name: "PLL3RGE")
                    .WithReservedBits( 12, 4 )
                    .WithValueField(16, 1, name: "DIVP1EN")
                    .WithValueField(17, 1, name: "DIVQ1EN")
                    .WithValueField(18, 1, name: "DIVR1EN")
                    .WithValueField(19, 1, name: "DIVP2EN")
                    .WithValueField(20, 1, name: "DIVQ2EN")
                    .WithValueField(21, 1, name: "DIVR2EN")
                    .WithValueField(22, 1, name: "DIVP3EN")
                    .WithValueField(23, 1, name: "DIVQ3EN")
                    .WithValueField(24, 1, name: "DIVR3EN")
                    .WithReservedBits( 25, 7 )
                },
                 {(long)Registers.RCC_PLL1DIVR, new DoubleWordRegister(this)//PLLCFGR
                    .WithValueField(0, 9, name: "DIVN1")
                    .WithValueField(9, 7, name: "DIVP1")
                    .WithValueField(16, 7, name: "DIVQ1")
                    .WithReservedBits( 23, 1 )
                    .WithValueField(24, 7, name: "DIVR1")
                    .WithReservedBits( 31, 1 )
                },
                {(long)Registers.RCC_PLL2DIVR, new DoubleWordRegister(this)//PLLCFGR
                    .WithValueField(0, 9, name: "DIVN2")
                    .WithValueField(9, 7, name: "DIVP2")
                    .WithValueField(16, 7, name: "DIVQ2")
                    .WithReservedBits( 23, 1 )
                    .WithValueField(24, 7, name: "DIVR2")
                    .WithReservedBits( 31, 1 )
                },
                {(long)Registers.RCC_PLL3DIVR, new DoubleWordRegister(this)//PLLCFGR
                    .WithValueField(0, 9, name: "DIVN3")
                    .WithValueField(9, 7, name: "DIVP3")
                    .WithValueField(16, 7, name: "DIVQ3")
                    .WithReservedBits( 23, 1 )
                    .WithValueField(24, 7, name: "DIVR3")
                    .WithReservedBits( 31, 1 )
                },




                // Add other registers here following the same pattern
            };

            RegistersCollection = new DoubleWordRegisterCollection(this, registersMap);
            Reset();
        }

        public void Reset()
        {
            RegistersCollection.Reset();
        }

        public DoubleWordRegisterCollection RegistersCollection { get; }

        public uint ReadDoubleWord(long offset)
        {
            return RegistersCollection.Read(offset);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            RegistersCollection.Write(offset, value);
        }

        public long Size => 0x400;

        private enum Registers
        {
            ClockControl = 0x00,
            PLLConfiguration = 0x04,
            ClockConfiguration = 0x10,
            DomainClockConfigurationR1 = 0x18,
            DomainClockConfigurationR2 = 0x1C,
            PllClkSrcSelectionR = 0x28,
            PllConfigR = 0x02C,

            RCC_PLL1DIVR = 0x030,
            RCC_PLL2DIVR = 0x038,
            RCC_PLL3DIVR = 0x040

        }
    }
}
