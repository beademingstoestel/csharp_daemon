# csharp_daemon

[![Build Status](https://dev.azure.com/TomBruyneel/TomBruyneel/_apis/build/status/beademingstoestel.csharp_daemon?branchName=master)](https://dev.azure.com/TomBruyneel/TomBruyneel/_build/latest?definitionId=2&branchName=master)

Communication layer between the interface and the arduino. Processes the incoming data and sends the results to the mongo db

This project can be built with visual studio and uses .NET core 3.1

Built binaries that are self-contained can be found at:

Windows 64 builds: https://dev.azure.com/TomBruyneel/beademingstoestel/_packaging?_a=feed&feed=daemon

For linux use the beademingstoestel/daemon image from [docker hub](https://hub.docker.com/#/beademingstoestel)

Works in tandem with the GUI which can be found at:

https://github.com/beademingstoestel/OpenSource_ventilator_lungs
