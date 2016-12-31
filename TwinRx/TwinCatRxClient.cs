using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        private readonly TcAdsClient _client;
        private readonly int _defaultCycleTime;
        private readonly int _maxDelay;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="client">TwinCAT ADS client. It should be connected</param>
        /// <param name="defaultCycleTime">Default cycle time the ADS client will poll for changes</param>
        /// <param name="maxDelay">Maximum ADS delay</param>
        public TwinCatRxClient(TcAdsClient client, int defaultCycleTime = 100, int maxDelay = 100)
        {
            _client = client;
            _defaultCycleTime = defaultCycleTime;
            _maxDelay = maxDelay;

            client.Synchronize = false; // This makes notifications come in on a ThreadPool thread instead of the UI thread
            client.AdsNotificationEx += client_AdsNotificationEx;
        }

        /// <summary>
        /// Creates an Observable for a PLC variable
        /// 
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
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="variableName">The full name of the PLC variable, i.e. "MAIN.var1"</param>
        /// <param name="cycleTime">Interval at which the ADS router will check the variable for changes (optional)</param>
        /// <returns>The BehaviorSubject as IObservable</returns>
        public IObservable<T> ObservableFor<T>(string variableName, int cycleTime = -1)
        {
            EnsureTypeIsSupported<T>();

            // Use Observable.Create together with RefCount() to be able to delete the device notification when all of
            // the observable's subscriptions are disposed.
            return Observable.Create<T>(observer =>
            {
                var subject = new Subject<T>();

                // Subscribe before we register the notification to avoid a race condition
                var subscription = subject.Subscribe(observer);

                var notification = new VariableNotification { subject = subject, type = typeof(T) };

                cycleTime = cycleTime == -1 ? _defaultCycleTime : cycleTime;

                // We do not use the return value (notification handle)
                int handleId;
                if (typeof(T) == typeof(string))
                {
                    handleId = _client.AddDeviceNotificationEx(variableName, AdsTransMode.OnChange, cycleTime, _maxDelay,
                        notification,
                        typeof(T), new[] { 256 });
                }
                else
                {
                    handleId = _client.AddDeviceNotificationEx(variableName, AdsTransMode.OnChange, cycleTime,  _maxDelay,
                        notification, typeof(T));
                }

                return new CompositeDisposable(
                    subscription,
                    Disposable.Create(() => _client.DeleteDeviceNotification(handleId))
                    );
            }).Replay().RefCount();
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

        /// <summary>
        /// Called by the TcAdsClient for variable change notifications
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        [SuppressMessage("ReSharper", "RedundantTypeArgumentsOfMethod")]
        void client_AdsNotificationEx(object sender, AdsNotificationExEventArgs e)
        {
            var notification = e.UserData as VariableNotification;
            if (notification == null) return; // This is a notification created externally to the TwinCatRxClient, ignore it
            
            // Read the variable associated with the
            HandleNotificationIfForType<bool>(notification, e);
            HandleNotificationIfForType<byte>(notification, e);
            HandleNotificationIfForType<sbyte>(notification, e);
            HandleNotificationIfForType<short>(notification, e);
            HandleNotificationIfForType<ushort>(notification, e);
            HandleNotificationIfForType<int>(notification, e);
            HandleNotificationIfForType<uint>(notification, e);
            HandleNotificationIfForType<float>(notification, e);
            HandleNotificationIfForType<double>(notification, e);
            HandleNotificationIfForType<string>(notification, e);
        }

        /// <summary>
        /// Somewhat type-safe handling of the notification if it is for the given type T
        /// </summary>
        /// <typeparam name="T">The notification should be handled if the variable is of this type</typeparam>
        /// <param name="notification">The variable notification info</param>
        /// <param name="e">ADS notification arguments</param>
        void HandleNotificationIfForType<T>(VariableNotification notification, AdsNotificationExEventArgs e)
        {
            if (notification.type != typeof(T)) return;

            var subject = (IObserver<T>)notification.subject;
            try
            {
                T value = (T)e.Value;
                subject.OnNext(value);
            }
            catch (Exception ex)
            {
                subject.OnError(ex);
            }
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
}
