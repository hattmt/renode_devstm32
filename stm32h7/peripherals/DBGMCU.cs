using System;
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public sealed class DBGMCU : IDoubleWordPeripheral, IKnownSize, IProvidesRegisterCollection<DoubleWordRegisterCollection>
    {
        public DBGMCU(IMachine machine )
        {

            var registersMap = new Dictionary<long, DoubleWordRegister>
            {
                {(long)Registers.OTG_GRSTCTL, new DoubleWordRegister(this,0)
                    .WithFlag(0, FieldMode.Read, valueProviderCallback: _ => false, name: "CSRST")
                    .WithFlag(1, out var HSRST, name: "PSRST")
                    .WithFlag(2, FieldMode.Read, name: "FCRST")
                    .WithReservedBits(3, 1)
                    .WithFlag(4,FieldMode.Read, valueProviderCallback: _ => false, name: "RXFFLSH")
                    .WithFlag(5,FieldMode.Read, valueProviderCallback: _ => false, name: "TXFFLSH")
                    .WithValueField(6, 5, name: "TXFNUM")
                    .WithReservedBits(11, 19)
                    .WithFlag(30, name: "DMAREQ")
                    .WithFlag(31, FieldMode.Read, valueProviderCallback: _ => true, name: "AHBIDL")
                },

                 {(long)Registers.OTG_GINTSTS, new DoubleWordRegister(this,0)
                    .WithFlag(0, FieldMode.Read, valueProviderCallback: _ => usb_mode, name: "CMOD")
                    .WithValueField(1, 31, name: "complete")
                }


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

        private bool usb_mode = true;//true= HOST, false = Device

        private enum Registers
        {

            OTG_GRSTCTL = 0x10,
            OTG_GINTSTS = 0x014


        }
    }
}
