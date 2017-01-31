using System;
using TwinCAT.Ads;
using Xunit;

namespace TwinRx.Tests
{
    public class WriteTests : IDisposable
    {
        private readonly TcAdsClient adsClient;
        private TwinCatRxClient client;

        public WriteTests()
        {
            adsClient = new TcAdsClient();
            adsClient.Connect(851);

            client = new TwinCatRxClient(adsClient);
        }

        public void Dispose()
        {
            client = null;
            adsClient.Dispose();
        }

        [Fact]
        public void ValueWritten()
        {
            // Create an observable for a PLC-updated variable
            client.Write<short>("MAIN.var3", 2);
            var plcVar = client.ObservableFor<short>("MAIN.var3", 100);

            var observer = new TestObserver<short>();
            plcVar.Subscribe(observer);

            client.Write<short>("MAIN.var3", 3);

            Assert.True(observer.HasReceivedValue());
            Assert.Equal(3, observer.LastReceivedValue);
        }

        [Fact]
        public void StringValueWritten()
        {
            // Create an observable for a PLC-updated variable
            client.Write("MAIN.var2", "abc");
            var plcVar = client.ObservableFor<string>("MAIN.var2", 100);

            var observer = new TestObserver<string>();
            plcVar.Subscribe(observer);

            client.Write("MAIN.var2", "abcdef");

            Assert.True(observer.HasReceivedValue());
            Assert.Equal("abcdef", observer.LastReceivedValue);
        }

        [Fact]
        public void StructValueWritten()
        {
            var var6 = "MAIN.var6";

            // Reset 
            client.Write(var6, new MyPlcStruct {myBool = false, myInt = 0});

            var plcVar = client.ObservableFor<MyPlcStruct>(var6, 100);

            var observer = new TestObserver<MyPlcStruct>();
            plcVar.Subscribe(observer);

            var myPlcStruct = new MyPlcStruct {myBool = true, myInt = 1};
            client.Write(var6, myPlcStruct);

            Assert.True(observer.HasReceivedValue());
            Assert.Equal(myPlcStruct, observer.LastReceivedValue);
        }
    }
}
