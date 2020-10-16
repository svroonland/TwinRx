## NOTE
_As of version 4.2.172.0, the Beckhoff ADS library for .NET also includes support for Reactive Extensions, making this library obsolete. See here for more information: https://infosys.beckhoff.com/english.php?content=../content/1033/tc3_adsnetref/7312584843.html&id=_

---

[![Build status](https://ci.appveyor.com/api/projects/status/edcc4iss127v7vnp?svg=true)](https://ci.appveyor.com/project/svroonland/twinrx)
[![NuGet version](https://badge.fury.io/nu/twinrx.svg)](https://badge.fury.io/nu/twinrx)

# TwinRx
TwinRx is a library for connecting a .NET application with a Beckhoff TwinCAT PLC program via Reactive Extensions (Rx) over ADS.

## Features
* Create an `IObservable` for a PLC variable, bringing changes to the PLC variable into the Reactive world.
* Make use of Rx's extensive event processing and querying capabilities to transform the observable into events of interest. 
* Stream (write) an existing `IObservable` to a PLC variable
* Transparently reregister the notifications after a connection loss

## Requires
* .NET 4.5 or higher
* Beckhoff TwinCAT PLC (v2 or v3)

## Installation
* Install the NuGet package "TwinRx" using the NuGet Package Manager in Visual Studio

## Example code
```c#
using System;
using System.Reactive.Linq;
using TwinCAT.Ads;
using TwinRx;

var adsClient = new TcAdsClient();
adsClient.Connect(801); // 801 for TwinCAT 2, 851 for TwinCAT 3

var client = new TwinCatRxClient(adsClient);

var counter = client.ObservableFor<short>("MAIN.var1", 20);

// Print out each value as it changes
counter.Subscribe(v => Console.WriteLine("Variable is now:" + v));

// Print out 10 values at a time
var buffered = counter.Buffer(10);
buffered.Subscribe(v => Console.WriteLine("Last 10 values were:" + String.Join(" - ", v)));

// Values including timestamp
var valuesWithTimestamp = counter.Select(i => new Tuple<short, DateTime>(i, DateTime.Now));
valuesWithTimestamp.Subscribe(Console.WriteLine);

// Take a single value each second
valuesWithTimestamp
	.Sample(TimeSpan.FromSeconds(5))
	.Subscribe(Console.WriteLine);

var myString = client.ObservableFor<string>("MAIN.var2");
myString.Subscribe(Console.WriteLine);

// Write a value to the PLC periodically
var valueEverySecond = Observable
	.Interval(TimeSpan.FromSeconds(1))
	.Select(i => (short) i);
var writer = client.StreamTo("MAIN.var3", valueEverySecond);

// Only even ones
var evens = client.ObservableFor<short>("MAIN.var4").Where(i => i%2 == 0);
var evensWithTimestamp = evens
	.Timestamp()
	.Zip(evens.TimeInterval(), (valWithTimestamp, interval) => new { val = "Even value is " + valWithTimestamp, interval });
evensWithTimestamp.Subscribe(Console.WriteLine);
```
