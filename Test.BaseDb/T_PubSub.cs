using KC.BaseDb;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Test.BaseDb
{
    public class T_PubSub
    {
        [SkippableFact]
        public async Task T_PubSub_Npg() {
            //Don't run this on appveyor, as there's no database
            Skip.If(Environment.GetEnvironmentVariable("isappveyor") != null);

            //This will throw if you're missing the test database, or unable to create it:
            NpgContext.WithContext(async db => {
                await db.Database.EnsureCreatedAsync();
                await db.Database.ExecuteSqlRawAsync("SELECT NULL");
            }).Wait();

            NpgContext.StartSubscriptionLoop(ex => {
                Assert.True(false, ex.Message);
            });

            await NpgContext.Notify("simpleTopic", "simplePayload");

            var lockCount = new object();
            var count = 0;

            var topic = ";DROP DATABASE PubSubTest;";
            NpgContext.Subscribe(topic, payload => {
                lock (lockCount) {
                    count += Convert.ToInt32(payload);
                }
            });

            //TODO: Optionally confirm subscription to prevent this.
            await Task.Delay(600); //Can take up to 500ms to have the subscription register.

            var sentCount = 0;
            for (int i = 1; i < 10; i++) {
                await Task.Delay(100);
                await NpgContext.Notify(topic, i.ToString());
                sentCount += i;
            }

            await Task.Delay(100);

            lock (lockCount) {
                Assert.Equal(sentCount, count);
            }
        }

        public class NpgContext : BaseDbContext<NpgContext> {
            public NpgContext() : base("PubSubTest", BaseDbType.Postgres, null, null, $"UserID=euler;Password=3.14159265358979323846264338327;Host=localhost;Port=5432;Database=PubSubTest;", null) {
            }
        }
    }
}
