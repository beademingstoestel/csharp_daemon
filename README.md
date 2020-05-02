# csharp_daemon

[![Build Status](https://dev.azure.com/TomBruyneel/TomBruyneel/_apis/build/status/beademingstoestel.csharp_daemon?branchName=master)](https://dev.azure.com/TomBruyneel/TomBruyneel/_build/latest?definitionId=2&branchName=master)

Communication layer between the interface and the arduino. Processes the incoming data and sends the results to the mongo db

This project can be built with visual studio and uses .NET core 3.1

Built binaries that are self-contained can be found at:

Windows 64: https://github.com/beademingstoestel/csharp_daemon/tree/master/VentilatorDaemon/publish

For linux use the beademingstoestel/daemon image from [docker hub](https://hub.docker.com/#/beademingstoestel)

Works in tandem with the GUI which can be found at:

https://github.com/beademingstoestel/OpenSource_ventilator_lungs
