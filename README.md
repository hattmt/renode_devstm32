# renode STM32 files development

Use Renode for stm32s emulation,
great tool which needs a lot of developpment but really promising ! 

Virtual debug on several boards , protocol sniffing, mmis screens emulation, buses (i2c/spi/canfd) , tests ,  ... : 

This repo add files for emulation of these stm32s :

- stm32g474 
- stm32g0b1
- stm32h7b0,stm32h750 with ltdc

You can directly load a file (.hex or .elf on it ) and debug with armgcc and vscode
for armclang (keil), I nedded to set the PC, SP and VectorTableOffset in my resc file after the file load.

  (ARMCC /armclang (keil) / armgcc )

  You can contribute and reuse the code for your own usage .
  It would be great to push this in official renode repository to contribute to its developement .

You need 2 main files :
- a .resc file which describe the board that you use, components (mcus ,leds, interconnections with buses ) , 
which files (hex , elf ) are you using on your uc...

- a .repl file which you call in your .resc file, and it describe all your mcu


Steps:

1) get renode last release:
https://github.com/renode/renode

2) read the doc and follow the first exemple here : https://renode.readthedocs.io/en/latest/introduction/demo.html

3) Develop Mcu's .repl script ( mcu  description (peripheral and core) )
   
   Most of cores are already defined in renode ( cortex m0/+ , m4 ,m7..)
   A lot of peripherals like  gpios , integrated memories , nvic,exti , ustarts ,spis are also already developped in renode.
   you just have to call them in your stmxxx.repl file (you can help yourself with renode installed folder , in "platforms/cpus" )

  Ex : 
 ``` cpu: CPU.CortexM @ sysbus
    cpuType: "cortex-m7"
    numberOfMPURegions: 16
    nvic: nvic
```

  Some peripharals are differents for each mcu and you need to implement them 
  
  Ex : 
  the stm32xxx RCC registers.

  for the stm32 rcc you must develop a Cs (C# file) with a class stm32xxx_rcc and you can call it directly from your  stmxxx.rep file

Your stm32h7b0_rcc.cs file :
![image](https://github.com/user-attachments/assets/b69768b9-6db5-46e1-bb7b-dbe0c8c65448)

import the class in your .resc file : ```i @stm32h7b0_rcc.cs```


and call it in your stm32h7b0.repl :

```
  rcc: Miscellaneous.STM32H7B0_RCC @ sysbus 0x58024400
  rtcPeripheral :true
```


