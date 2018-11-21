using KC.BaseDb.Internal;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KC.BaseDb {
    public enum BaseDbType {
        Sqlite,
        Postgres,
        SqlServer,
        Other
    }

    public abstract class BaseDbContext<SELF> : DbContext 
            where SELF: BaseDbContext<SELF>, new()
        {

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

    }
}
