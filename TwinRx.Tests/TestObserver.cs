using System;
using System.Threading;

namespace TwinRx.Tests
{
    class TestObserver<T> : IObserver<T>
    {
        readonly AutoResetEvent _valueReceived = new AutoResetEvent(false);
        readonly AutoResetEvent _errorReceived = new AutoResetEvent(false);

        public void OnCompleted()
        {

        }

        public void OnError(Exception error)
        {
            Console.WriteLine(error);
            _errorReceived.Set();
        }

        public void OnNext(T value)
        {
            LastReceivedValue = value;
            _valueReceived.Set();
        }

        public bool HasReceivedValue(int timeout = 1000)
        {
            return _valueReceived.WaitOne(timeout);
        }

        public bool HasReceivedError(int timeout = 1000)
        {
            return _errorReceived.WaitOne(timeout);
        }

        public T LastReceivedValue { get; private set; }
    }
}