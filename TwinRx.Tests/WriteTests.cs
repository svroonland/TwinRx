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
            adsClient.Connect(801);

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

    }
}
