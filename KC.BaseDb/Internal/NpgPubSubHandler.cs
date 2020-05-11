using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
        public void StartPubSubLoop(Action<Exception> logException = null) {
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

        public void StopPubSubLoop() {
            lock (lockSubscriptionLoopSequence) {
                subscriptionLoopSequence++;
            }
        }

        static ThreadLocal<MD5> m_md5 = new ThreadLocal<MD5>(() => MD5.Create());
        static string getMd5Hash(string input) {
            var md5 = m_md5.Value;
            byte[] data = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            StringBuilder sBuilder = new StringBuilder();
            sBuilder.Append('_');
            for (int i = 0; i < data.Length; i++) {
                sBuilder.Append(data[i].ToString("x2"));
            }
            return sBuilder.ToString();
        }

        private ReaderWriterLockSlim lockSubscriptions = new ReaderWriterLockSlim();
        private Dictionary<string, Dictionary<Guid, Action<string>>> subscriptions = new Dictionary<string, Dictionary<Guid, Action<string>>>();
        public IDisposable Subscribe(string topic, Action<string> callback) {
            if (topic == null || callback == null) {
                throw new ArgumentNullException("topic or callback");
            }
            topic = getMd5Hash(topic); //IMPORTANT: This prevents SQL injection. You must not remove this unless you account for that.
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

        public async Task Publish(IEnumerable<Notification> notifications) {
            if (notifications == null || !notifications.Any()) {
                return;
            }
            await BaseDbContext<DBCONTEXT>.WithContext(async db => {
                var sb = new StringBuilder();
                var count = 0;
                foreach (var notification in notifications) {
                    count++;
                    sb.AppendLine($"SELECT pg_notify('{getMd5Hash(notification.Topic)}', @p{count});");
                }

                await db.Database.OpenConnectionAsync();
                var conn = (NpgsqlConnection)db.Database.GetDbConnection();

                var cmd = new NpgsqlCommand(sb.ToString(), conn);

                count = 0;
                foreach (var notification in notifications) {
                    count++;
                    cmd.Parameters.Add(new NpgsqlParameter<string>($"@p{count}", notification.Payload));
                }

                await cmd.ExecuteNonQueryAsync();
            });
        }
    }
}
