using System;
using System.Reactive.Subjects;
using TwinCAT.Ads;
using Xunit;

namespace TwinRx.Tests
{
    public class StreamTests : IDisposable
    {
        private readonly TcAdsClient adsClient;
        private TwinCatRxClient client;

        public StreamTests()
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
            // Reset
            client.Write<short>("MAIN.var3", 2);

            // Create an observable for a PLC-updated variable
            var plcVar = client.ObservableFor<short>("MAIN.var3", 100);
            var observer = new TestObserver<short>();
            plcVar.Subscribe(observer);

            // Push values onto the observable
            var subject = new Subject<short>();
            client.StreamTo("MAIN.var3", subject);

            subject.OnNext(3);

            Assert.True(observer.HasReceivedValue());
            Assert.Equal(3, observer.LastReceivedValue);
        }

        [Fact]
        public void ValueNotWrittenAfterUnsubscribed()
        {
            // Reset
            client.Write<short>("MAIN.var3", 2);

            // Create an observable for a PLC-updated variable
            var plcVar = client.ObservableFor<short>("MAIN.var3", 100);
            var observer = new TestObserver<short>();
            var subscription = plcVar.Subscribe(observer);

            // Push values onto the observable
            var subject = new Subject<short>();
            client.StreamTo("MAIN.var3", subject);

            subscription.Dispose();
            subject.OnNext(3);

            Assert.False(observer.HasReceivedValue());
            
        }

    }
}
