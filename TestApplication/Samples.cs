using System;
using System.Reactive.Linq;
using TwinCAT.Ads;
using TwinRx;

namespace TestApplication
{
    /**
     * Sample making use of test.pro
     **/
    public class Samples
    {
        // ReSharper disable once UnusedParameter.Local
        static void Main(string[] args)
        {
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

            Console.WriteLine("Connected, press key to exit");
            Console.ReadKey();

            writer.Dispose();
            adsClient.Dispose();
        }
    }
}
