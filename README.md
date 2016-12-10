# TwinRx
TwinRx is a library for connecting a .NET application with a Beckhoff TwinCAT PLC program via Reactive Extensions (Rx) over ADS.

## Features
* Create an `IObservable` for a PLC variable, bringing changes to the PLC variable into the Reactive world.
* Make use of Rx's extensive event processing and querying capabilities to transform the observable into events of interest. 
* Stream (write) an existing `IObservable` to a PLC variable

## Requires
* .NET 4.5 or higher
* System.Reactive extensions: https://msdn.microsoft.com/library/hh242985.aspx
* Beckhoff TwinCAT PLC (v2 or v3)

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
