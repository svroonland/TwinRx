using System;
using System.Reactive.Linq;

namespace TwinRx
{
    public static class ObservableExtensions
    {
        /// <summary>
        /// Recreate the `source` observable on every event emitted in `trigger`
        /// </summary>
        /// <typeparam name="T">Type of source observable</typeparam>
        /// <typeparam name="TTrigger">Type of trigger observable</typeparam>
        /// <param name="source">Source observable</param>
        /// <param name="trigger">Trigger observable</param>
        /// <returns></returns>
        public static IObservable<T> RecreateOn<T, TTrigger>(this IObservable<T> source, IObservable<TTrigger> trigger)
        {
            return trigger.StartWith(default(TTrigger))
                .Select(_ => source.TakeUntil(trigger))
                .Concat();
        }
    }
}