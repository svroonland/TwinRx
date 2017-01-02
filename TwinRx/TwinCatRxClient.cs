using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using TwinCAT.Ads;

namespace TwinRx
{
    /// <summary>
    /// Wrapper around a TwinCAT ADS client to support TwinCAT PLC variables as Rx Observables
    /// </summary>
    public class TwinCatRxClient
    {
        private TcAdsClient _client;
        private readonly int _defaultCycleTime;
        private readonly int _maxDelay;
        private IObservable<EventPattern<AdsNotificationExEventArgs>> _notifications;
        readonly Subject<Unit> _reconnectEvents = new Subject<Unit>();

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="client">TwinCAT ADS client. It should be connected</param>
        /// <param name="defaultCycleTime">Default cycle time the ADS client will poll for changes</param>
        /// <param name="maxDelay">Maximum ADS delay</param>
        public TwinCatRxClient(TcAdsClient client, int defaultCycleTime = 100, int maxDelay = 100)
        {
            _defaultCycleTime = defaultCycleTime;
            _maxDelay = maxDelay;

            OnIninitialize(client);
        }

        private void OnIninitialize(TcAdsClient client)
        {
            _client = client;
            _client.Synchronize = false; // This makes notifications come in on a ThreadPool thread instead of the UI thread

            _notifications = Observable.FromEventPattern<AdsNotificationExEventHandler, AdsNotificationExEventArgs>(
                h => _client.AdsNotificationEx += h, h => _client.AdsNotificationEx -= h);
        }

        /// <summary>
        /// Re-register all ADS notifications upon a new TcAdsClient without having to recreate PLC variable observables
        /// </summary>
        /// <remarks>
        /// Use after a lost ADS connection has been established and a new client connection is established. Existing notifications are
        /// unregistered, ignoring any exceptions on the old client
        /// </remarks> 
        /// <param name="client">A new TwinCAT ADS client. It should be connected</param>
        public void Reconnect(TcAdsClient client)
        {
            OnIninitialize(client);
            _reconnectEvents.OnNext(Unit.Default);
        }

        /// <summary>
        /// Creates an Observable for a PLC variable
        /// </summary>
        /// 
        /// <remarks>
        /// Values are emitted when the PLC variable changes, with a minimum interval of `cycleTime` ms.
        /// 
        /// Created observables are ReplaySubjects, Subjects that always replay the latest value to new subscribers. This
        /// is done to avoid race conditions between subscription and the first ADS notification. It also ensures that 
        /// subscribers always have access to the latest value of the PLC variable
        /// 
        /// TwinCAT ADS's notification mechanism will always do an initial notification with the current variable's value
        /// 
        /// The actual ADS variable handle is only created when the first subscription to the Observable is made. The handle
        /// is deleted only when the last subscription is disposed. 
        /// 
        /// Depending on the client's usage pattern, it's recommended to dispose any subscriptions. Alternatively
        /// they will be disposed when the application exists.
        /// 
        /// When Reconnect() is called, the ADS notification is unregistered and reregistered.
        /// 
        /// Supported PLC variable types are, together with their .NET types:
        /// * BOOL (bool)
        /// * BYTE (byte)
        /// * SINT (sbyte)
        /// * USINT (byte)
        /// * INT (short)
        /// * UINT (ushort)
        /// * DINT (int)
        /// * UDINT (uint)
        /// * REAL (float)
        /// * LREAL (double)
        /// * STRING (string)
        /// </remarks>
        /// <typeparam name="T">.NET type of the variable</typeparam>
        /// <param name="variableName">The full name of the PLC variable, i.e. "MAIN.var1"</param>
        /// <param name="cycleTime">Interval at which the ADS router will check the variable for changes (optional)</param>
        /// <returns>Observable that emits when the PLC variable changes</returns>
        public IObservable<T> ObservableFor<T>(string variableName, int cycleTime = -1)
        {
            EnsureTypeIsSupported<T>();

            var createObservable = new Func<IObservable<T>>(() => Observable.Using(() => CreateNotificationRegistration<T>(variableName, cycleTime),
                r =>
                    _notifications.Select(e => e.EventArgs).Where(e => e.NotificationHandle == r.HandleId)
                        .Select(e => (T) e.Value)).Replay().RefCount());

            // Create now and recreate the observable for every reconnect. The TakeUntil takes care of proper disposal after a reconnect
            return _reconnectEvents.StartWith(Unit.Default)
                .Select(_ => createObservable().TakeUntil(_reconnectEvents))
                // Make sure the previous observable is ended at a reconnect
                .Concat();
        }

        private static void EnsureTypeIsSupported<T>()
        {
            var supportedTypes = new List<Type>()
            {
                typeof (bool),
                typeof(byte),
                typeof(sbyte),
                typeof (short),
                typeof (ushort),
                typeof (int),
                typeof (uint),
                typeof (float),
                typeof (double),
                typeof (string)
            };

            if (!supportedTypes.Contains(typeof(T)))
            {
                throw new InvalidOperationException("Variables of type " + typeof(T) + " are not supported");
            }
        }

        private NotificationRegistration CreateNotificationRegistration<T>(string variableName, int cycleTime)
        {
            return new NotificationRegistration(RegisterNotificationHandle<T>(variableName, cycleTime), _client);
        }

        private int RegisterNotificationHandle<T>(string variableName, int cycleTime)
        {
            cycleTime = cycleTime == -1 ? _defaultCycleTime : cycleTime;

            if (typeof(T) == typeof(string))
            {
                return _client.AddDeviceNotificationEx(variableName, AdsTransMode.OnChange, cycleTime, _maxDelay,
                    null,
                    typeof(T), new[] { 256 });
            }
            return _client.AddDeviceNotificationEx(variableName, AdsTransMode.OnChange, cycleTime, _maxDelay,
                null, typeof(T));
        }

        /// <summary>
        /// Subscribe to an observable and write the emitted values to a PLC variable
        /// 
        /// Use this method to regularly write values to PLC variable. For one-off writes, use the Write() method
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="variableName">Name of the PLC variable</param>
        /// <param name="observable">Observable that emits values to write</param>
        /// <param name="scheduler">Scheduler to execute the subscription on. By default uses the scheduler the Observable runs on</param>
        /// <returns>An IDisposable that can be disposed when it's no longer desired to write the values in the observable</returns>
        public IDisposable StreamTo<T>(string variableName, IObservable<T> observable, IScheduler scheduler = null)
        {
            EnsureTypeIsSupported<T>();

            scheduler = scheduler ?? Scheduler.Immediate;

            int variableHandle = _client.CreateVariableHandle(variableName);

            var subscription = observable.ObserveOn(scheduler).Subscribe(value => WriteWithHandle(variableHandle, value));

            // Return an IDisposable that, when disposed, stops listening to the Observable but also deletes the variable handle
            return new CompositeDisposable(subscription, Disposable.Create(() => _client.DeleteVariableHandle(variableHandle)));
        }

        /// <summary>
        /// Writes a value to a PLC variable once
        /// 
        /// This method is a convenience method over the TcAdsClient. Use it method to write to values once, consider using StreamTo
        /// to regularly write values (using a source observable)
        /// 
        /// The write is executed on the ThreadPool and can optionally be observed to know when the write is done
        /// </summary>
        /// <typeparam name="T">Type of the value</typeparam>
        /// <param name="variableName">Name of the PLC variable</param>
        /// <param name="value">Value to write</param>
        /// <param name="scheduler">Scheduler to execute the write to, by default uses the system's default scheduler</param>
        public IObservable<Unit> Write<T>(string variableName, T value, IScheduler scheduler = null)
        {
            return Observable.Start(() =>
            {
                try
                {
                    WriteWithHandle(_client.CreateVariableHandle(variableName), value);
                }
                finally
                {
                    _client.DeleteVariableHandle(_client.CreateVariableHandle(variableName));
                }
            }, scheduler ?? Scheduler.Default);
        }

        private void WriteWithHandle<T>(int variableHandle, T value)
        {
            if (typeof(T) == typeof(string))
            {
                // ReSharper disable once PossibleNullReferenceException
                _client.WriteAny(variableHandle, value, new[] { (value as string).Length });
            }
            else
            {
                _client.WriteAny(variableHandle, value);
            }
        }
    }

    /**
     * IDisposable for ADS notification registrations
     * 
     * When disposed, deletes the ADS notification
     **/
    class NotificationRegistration : IDisposable
    {
        public int HandleId { get; private set; }
        private TcAdsClient _client;

        public NotificationRegistration(int handleId, TcAdsClient client)
        {
            HandleId = handleId;
            _client = client;
        }

        public void Dispose()
        {
            try
            {
                _client.DeleteDeviceNotification(HandleId);
            }
            catch
            {
                // ignored. An exception may happen when the ADS connection is lost
            }
            finally
            {
                _client = null;
            }
        }
    }
}
