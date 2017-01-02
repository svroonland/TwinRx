using System;
using TwinCAT.Ads;
using Xunit;

namespace TwinRx.Tests
{
    public class ObservableForTests : IDisposable
    {
        private readonly TcAdsClient adsClient;
        private TwinCatRxClient client;

        public ObservableForTests()
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
        public void ValueReceived()
        {
            // Create an observable for a PLC-updated variable
            var observable = client.ObservableFor<short>("MAIN.var1", 100);

            var observer = new TestObserver<short>();
            observable.Subscribe(observer);

            Assert.True(observer.HasReceivedValue());
        }

        [Fact]
        public void InitialValueAvailableWithoutChange()
        {
            // Create an observable for a variable that is not updated by the PLC program
            var observable = client.ObservableFor<short>("MAIN.var3", 100);

            var observer = new TestObserver<short>();
            observable.Subscribe(observer);

            Assert.True(observer.HasReceivedValue());
        }

        [Fact]
        public void ObservableCreatedOnSubscribe()
        {
            // Create an observable for a non-existing variable
            client.ObservableFor<short>("MAIN.varNonExist", 100);
        }

        [Fact]
        public void ObservableCreatedOnSubscribeError()
        {
            // Create an observable for a non-existing variable
            var observable = client.ObservableFor<short>("MAIN.varNonExist", 100);

            var observer = new TestObserver<short>();
            observable.Subscribe(observer);

            Assert.True(observer.HasReceivedError());
        }

        [Fact]
        public void ExceptionForUnsupportedType()
        {
            Assert.Throws<ArgumentException>(() => client.ObservableFor<long>("MAIN.var1", 100));
        }

        [Fact]
        public void StringObservable()
        {
            // Create an observable for a PLC-updated variable
            var observable = client.ObservableFor<string>("MAIN.var2", 100);

            var observer = new TestObserver<string>();
            observable.Subscribe(observer);

            Assert.True(observer.HasReceivedValue());
        }
    }
}
