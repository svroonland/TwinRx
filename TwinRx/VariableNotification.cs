using System;

namespace TwinRx
{
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