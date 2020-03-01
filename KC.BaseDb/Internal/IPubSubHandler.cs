using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace KC.BaseDb.Internal {
    interface IPubSubHandler {
        void StartSubscriptionLoop(Action<Exception> logException = null);
        void StopSubscriptionLoop();
        Task Notify(IEnumerable<Notification> notifications);
        IDisposable Subscribe(string topic, Action<string> callback);
    }
}
