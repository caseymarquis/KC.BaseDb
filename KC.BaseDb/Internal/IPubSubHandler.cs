using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace KC.BaseDb.Internal {
    interface IPubSubHandler {
        void StartPubSubLoop(Action<Exception> logException = null);
        void StopPubSubLoop();
        Task Publish(IEnumerable<Notification> notifications);
        IDisposable Subscribe(string topic, Action<string> callback);
    }
}
