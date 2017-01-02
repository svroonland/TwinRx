using System;
using TwinCAT.Ads;

namespace TwinRx
{
    /// <summary>
    /// IDisposable for ADS notification registrations
    /// 
    /// When disposed, deletes the ADS notification
    /// </summary>
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