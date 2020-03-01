using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace KC.BaseDb.Internal {
    class NpgPubSubHandler<DBCONTEXT> : IPubSubHandler where DBCONTEXT : BaseDbContext<DBCONTEXT>, new() {
        private class SubscriptionHandle : IDisposable {
            private Action doDispose;
            public SubscriptionHandle(Action doDispose) {
                this.doDispose = doDispose;
            }
            public void Dispose() {
                doDispose();
            }
        }

        private object lockChannels = new object();
        private HashSet<string> subscribedChannels = new HashSet<string>();
        private HashSet<string> requiredChannels = new HashSet<string>();

        private object lockSubscriptionLoopSequence = new object();
        private int subscriptionLoopSequence;
        private int lastSubscriptionLoopSequence = -1;
        public void StartSubscriptionLoop(Action<Exception> logException = null) {
            lock (lockSubscriptionLoopSequence) {
                if (subscriptionLoopSequence == lastSubscriptionLoopSequence) {
                    return;
                }
                subscriptionLoopSequence++;
                var mySubLoopSequence = subscriptionLoopSequence;
                var success = false;
                try {
                    var t = new Thread(() => {
                        bool shouldRun() {
                            lock (lockSubscriptionLoopSequence) return subscriptionLoopSequence == mySubLoopSequence;
                        }
                        while (shouldRun()) {
                            try {
                                lock (lockChannels) {
                                    subscribedChannels = new HashSet<string>();
                                }
                                using (var db = new DBCONTEXT()) {
                                    var connection = (NpgsqlConnection)db.Database.GetDbConnection();
                                    connection.Open();
                                    connection.Notification += (object sender, NpgsqlNotificationEventArgs e) => {
                                        try {
                                            List<Action<string>> callbackList = null;
                                            lockSubscriptions.EnterReadLock();
                                            try {
                                                if (subscriptions.TryGetValue(e.Channel, out var callbacks)) {
                                                    callbackList = callbacks.Values.ToList();
                                                }
                                            }
                                            finally {
                                                lockSubscriptions.ExitReadLock();
                                            }
                                            if (callbackList != null) {
                                                foreach (var callback in callbackList) {
                                                    try {
                                                        callback?.Invoke(e.Payload ?? "");
                                                    }
                                                    catch (Exception ex) {
                                                        logException?.Invoke(ex);
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception ex) {
                                            logException?.Invoke(ex);
                                        }
                                    };

                                    var lastKeepAlive = DateTimeOffset.Now;
                                    while (shouldRun()) {
                                        string[] channelsToAdd;
                                        string[] channelsToRemove;
                                        lock (lockChannels) {
                                            channelsToAdd = requiredChannels.Except(subscribedChannels).ToArray();
                                            channelsToRemove = subscribedChannels.Except(requiredChannels).ToArray();
                                        }

                                        if (channelsToAdd.Any() || channelsToRemove.Any()) {
                                            //Because we escape the channel, we don't need to use parameters to avoid injection:
                                            var removeCmd = string.Join(" ", channelsToRemove.Select(x => $"UNLISTEN {x};"));
                                            var addCmd = string.Join(" ", channelsToAdd.Select(x => $"LISTEN {x};"));
                                            var transactionSuccess = false;
                                            db.Database.BeginTransaction();
                                            try {
                                                db.Database.ExecuteSqlRaw(removeCmd + addCmd);
                                                transactionSuccess = true;
                                            }
                                            finally {
                                                if (transactionSuccess) {
                                                    db.Database.CommitTransaction();
                                                }
                                                else {
                                                    db.Database.RollbackTransaction();
                                                }
                                            }
                                            lock (lockChannels) {
                                                foreach (var channelToRemove in channelsToRemove) {
                                                    subscribedChannels.Remove(channelToRemove);
                                                }
                                                foreach (var channelToAdd in channelsToAdd) {
                                                    subscribedChannels.Add(channelToAdd);
                                                }
                                            }
                                        }

                                        if ((DateTimeOffset.Now - lastKeepAlive).TotalSeconds > 30) { //TODO: Setting for keepalive timeout
                                            lastKeepAlive = DateTimeOffset.Now;
                                            db.Database.ExecuteSqlRaw("SELECT NULL"); //If the connection has broken, this will throw an error.
                                        }
                                        connection.Wait(500); //TODO: Should this timeout be adjustable? It's the max rate you can subscribe to something new.
                                    }
                                };
                            }
                            catch (Exception ex) {
                                logException?.Invoke(ex);
                            }
                        }
                    });
                    t.IsBackground = true;
                    t.Start();
                    success = true;
                }
                finally {
                    if (!success) {
                        subscriptionLoopSequence++;
                    }
                }
            }
        }

        public void StopSubscriptionLoop() {
            lock (lockSubscriptionLoopSequence) {
                subscriptionLoopSequence++;
            }
        }

        private string escapeTopic(string topic) {
            var sb = new StringBuilder();
            sb.Append('_');
            foreach (var c in topic) {
                if ('a' <= c && c <= 'z' || 'A' <= c && c <= 'Z' || '0' <= c && c <= '9' || c == '_') {
                    sb.Append(c);
                }
                else {
                    sb.Append("_esc_");
                    sb.Append(((int)c).ToString());
                    sb.Append("_esc_");
                }
            }
            return sb.ToString();
        }

        private ReaderWriterLockSlim lockSubscriptions = new ReaderWriterLockSlim();
        private Dictionary<string, Dictionary<Guid, Action<string>>> subscriptions = new Dictionary<string, Dictionary<Guid, Action<string>>>();
        public IDisposable Subscribe(string topic, Action<string> callback) {
            if (topic == null || callback == null) {
                throw new ArgumentNullException("topic or callback");
            }
            topic = escapeTopic(topic); //IMPORTANT: This prevents SQL injection. You must not remove this unless you account for that.
            var callbackId = Guid.NewGuid();
            var disposeHandle = new SubscriptionHandle(() => {
                lockSubscriptions.EnterWriteLock();
                try {
                    if (subscriptions.TryGetValue(topic, out var callbackList)) {
                        callbackList.Remove(callbackId);
                        if (!callbackList.Any()) {
                            lock (lockChannels) {
                                requiredChannels.Remove(topic);
                            }
                        }
                    }
                }
                finally {
                    lockSubscriptions.ExitWriteLock();
                }
            });

            lockSubscriptions.EnterWriteLock();
            try {
                if (!subscriptions.TryGetValue(topic, out var callbacks)) {
                    callbacks = new Dictionary<Guid, Action<string>>();
                    subscriptions.Add(topic, callbacks);
                }
                if (!callbacks.Any()) {
                    lock (lockChannels) {
                        requiredChannels.Add(topic);
                    }
                }
                callbacks.Add(callbackId, callback);
                return disposeHandle;
            }
            finally {
                lockSubscriptions.ExitWriteLock();
            }
        }
    }
}
