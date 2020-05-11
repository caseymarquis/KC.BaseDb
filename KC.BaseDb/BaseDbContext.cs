using KC.BaseDb.Internal;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KC.BaseDb {
    public enum BaseDbType {
        Sqlite,
        Postgres,
        SqlServer,
        Other
    }

    public abstract class BaseDbContext<SELF> : DbContext
            where SELF : BaseDbContext<SELF>, new() {

        public static BaseDbType BaseDbType => DbConnectionSettings.BaseDbType;

        /// <summary>
        /// Don't use this directly!
        /// This constructor allows NuGet to migrate/update/etc the database.
        /// </summary>
        public BaseDbContext(string appName, BaseDbType baseDbType, string connectionStringEnvironmentVariable, string connectionStringFilePath, string hardCodedConnectionString = null, Action<DbContextOptionsBuilder<SELF>> doCustomSetup = null)
            : base(getDbOptions(baseDbType, DbConnectionSettings.UpdateConnectionString(appName, baseDbType, connectionStringEnvironmentVariable, connectionStringFilePath, hardCodedConnectionString), doCustomSetup)) {
        }

        private static DbContextOptions getDbOptions(BaseDbType baseDbType, string connectionString, Action<DbContextOptionsBuilder<SELF>> doCustomSetup) {
            var builder = new DbContextOptionsBuilder<SELF>() {

            };
            switch (baseDbType) {
                case BaseDbType.Postgres:
                    builder.UseNpgsql(connectionString);
                    break;
                case BaseDbType.Sqlite:
                    builder.UseSqlite(connectionString);
                    break;
                case BaseDbType.SqlServer:
                    builder.UseSqlServer(connectionString);
                    break;
                case BaseDbType.Other:
                    if (doCustomSetup == null) {
                        throw new ApplicationException("You must pass some logic in with the doCustomSetup argument if using type 'Other'");
                    }
                    break;
            }
            doCustomSetup?.Invoke(builder);
            return builder.Options;
        }

        public static readonly DbConnectionSettings DbConnectionSettings = new DbConnectionSettings();
        private static AsyncReaderWriterLock sqliteLock = new AsyncReaderWriterLock();

        public static async Task<T> WithContext<T>(Func<SELF, Task<T>> getSomething, bool isWrite = true) {
            var dbType = DbConnectionSettings.BaseDbType;
            Action lockRelease = null;
            if (dbType == BaseDbType.Sqlite) {
                lockRelease = await (isWrite ? sqliteLock.EnterWriteLock() : sqliteLock.EnterReadLock());
            }
            try {
                using (var db = new SELF()) {
                    return await getSomething(db);
                }
            }
            finally {
                if (lockRelease != null) {
                    lockRelease();
                }
            }
        }

        public static async Task WithContext(Func<SELF, Task> doSomething, bool isWrite = true) {
            await WithContext(async (db) => {
                await doSomething(db);
                return 0;
            }, isWrite);
        }

        public static async Task Migrate() {
            await WithContext(async db => {
                await db.Database.MigrateAsync();
            });
        }

        public static bool CanStartPubSubLoop() {
            return BaseDbType == BaseDbType.Postgres;
        }

        private static ReaderWriterLockSlim lockPubSubHandler = new ReaderWriterLockSlim();
        private static IPubSubHandler pubSubHandler;
        public static void StartPubSubLoop(Action<Exception> logException = null) {
            lockPubSubHandler.EnterUpgradeableReadLock();
            try {
                if (pubSubHandler == null) {
                    lockPubSubHandler.EnterWriteLock();
                    try {
                        if (CanStartPubSubLoop()) {
                            switch (BaseDbType) {
                                case BaseDbType.Postgres:
                                    pubSubHandler = new NpgPubSubHandler<SELF>();
                                    break;
                                default:
                                    throw new ApplicationException($"BaseDb core logic error. PubSub started with database type {BaseDbType.ToString("g")}");
                            }
                        }
                        else {
                            throw new ApplicationException($"Database type {BaseDbType.ToString("g")} does not support PubSub.");
                        }
                    }
                    finally {
                        lockPubSubHandler.ExitWriteLock();
                    }
                }
                pubSubHandler.StartPubSubLoop(logException);
            }
            finally {
                lockPubSubHandler.ExitUpgradeableReadLock();
            }
        }

        public static void StopPubSubLoop() {
            lockPubSubHandler.EnterReadLock();
            try {
                pubSubHandler?.StopPubSubLoop();
            }
            finally {
                lockPubSubHandler.ExitReadLock();
            }
        }

        public static IDisposable Subscribe(string topic, Action<string> callback) {
            lockPubSubHandler.EnterReadLock();
            try {
                if (pubSubHandler == null) {
                    throw new ApplicationException("You must call 'StartSubscriptionLoop' before calling Subscribe.");
                }
                return pubSubHandler.Subscribe(topic, callback);
            }
            finally {
                lockPubSubHandler.ExitReadLock();
            }
        }

        public static async Task Publish(string topic, string payload = null) {
            await Notify(new Notification[] {
                new Notification{
                    Topic = topic,
                    Payload = payload,
                }
            });
        }

        public static async Task Notify(IEnumerable<Notification> notifications) {
            IPubSubHandler psh;
            lockPubSubHandler.EnterReadLock();
            try {
                if (pubSubHandler == null) {
                    throw new ApplicationException("You must call 'StartSubscriptionLoop' before calling Notify.");
                }
                psh = pubSubHandler;
            }
            finally {
                lockPubSubHandler.ExitReadLock();
            }
            await pubSubHandler.Publish(notifications);
        }
    }
}
