using KC.BaseDb;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Test.BaseDb {
    public class T_PGSqlUrlParse {
        [Fact]
        public void PGSqlUrlParse() {
            Environment.SetEnvironmentVariable(
                "DBSTRING",
                "postgresql://user:pass@192.168.40.241:5432/DbName?sslmode=disable");
            var connectionString = new DbConnectionSettings().UpdateConnectionString("appName", BaseDbType.Postgres, "DBSTRING", null, null);
            Assert.StartsWith("Server=192.168.40.241;Port=5432;Database=DbName;Userid=user;Password=pass;", connectionString);
            Assert.Contains("sslmode=disable;", connectionString);
            Assert.Contains("TrustServerCertificate=true;", connectionString);
        }
    }
}
