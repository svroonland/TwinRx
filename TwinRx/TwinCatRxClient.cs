using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Disposables;
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

            client.Synchronize = false;
            client.AdsNotificationEx += client_AdsNotificationEx;
        }

        /// <summary>
        /// Creates an Observable for a PLC variable
        /// 
        /// Created observables are ReplaySubjects, Subjects that always replay the latest value to new subscribers. This
        /// is done to avoid race conditions between subscription and the first ADS notification. It also ensures that 
        /// subscribers always have access to the latest value of the PLC variable
        /// 
        /// TwinCAT ADS's notification mechanism will always do an initial notification with the current variable's value
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
            EnsureAdsClientIsConnected();
            EnsureTypeIsSupported<T>();

            var subject = new ReplaySubject<T>(1);

            var notification = new VariableNotification { subject = subject, type = typeof(T) };

            cycleTime = cycleTime == -1 ? _defaultCycleTime : cycleTime;

            // We do not use the return value (notification handle)
            if (typeof(T) == typeof(string))
            {
                _client.AddDeviceNotificationEx(variableName, AdsTransMode.OnChange, cycleTime, _maxDelay, notification,
                    typeof(T), new[] { 256 });
            }
            else
            {
                _client.AddDeviceNotificationEx(variableName, AdsTransMode.OnChange, cycleTime, _maxDelay, notification, typeof(T));
            }

            return subject;
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

            var subject = (ReplaySubject<T>)notification.subject;
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
        /// Observe an observable and write values to a PLC variable
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="variableName"></param>
        /// <param name="observable"></param>
        /// <returns></returns>
        public IDisposable StreamTo<T>(string variableName, IObservable<T> observable)
        {
            EnsureAdsClientIsConnected();
            EnsureTypeIsSupported<T>();

            int variableHandle = _client.CreateVariableHandle(variableName);

            var subscription = observable.Subscribe(value =>
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
            });

            // Return an IDisposable that, when disposed, stops listening to the Observable but also deletes the variable handle
            return Disposable.Create(() =>
            {
                subscription.Dispose();
                _client.DeleteVariableHandle(variableHandle);
            });
        }

        private void EnsureAdsClientIsConnected()
        {
            if (!_client.IsConnected) throw new InvalidOperationException("TcAdsClient is not connected");
        }
    }

    /// <summary>
    /// Information about a variable notification: its type and its Subject
    /// </summary>
    internal class VariableNotification
    {
        /// <summary>
        /// Type of the variable
        /// </summary>
        public Type type;

        /// <summary>
        /// The Subject[Type] instance, where Type is not known statically
        /// </summary>
        public object subject;
    }
}
