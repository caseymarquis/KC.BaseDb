using System;
using System.Collections.Generic;
using System.Text;

namespace KC.BaseDb.Internal {
    interface IPubSubHandler {
        void StartSubscriptionLoop(Action<Exception> logException = null);
        void StopSubscriptionLoop();
        IDisposable Subscribe(string topic, Action<string> callback);
    }
}
