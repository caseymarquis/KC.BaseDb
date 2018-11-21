# BaseDb

This library eliminates some of the boilerplate from using Entity Framework (EF).
It provides:
* The ability to easily get a connection string from either environment variables, or a file, or a hard coded string; with attempts in that order.
* A static convenience function for migrating the database on app start.
* Static convenience functions for accessing and disposing the database.
* Static read/write locking if the underlying database is Sqlite.
* All packages needed to run migrations in visual studio, and to access Postgres, SqlServer, and Sqlite databases. (If you need something outside of those)

That's pretty much it, but 

* AllProperties: All public properties which were not ignored with an attribute.
* ValueAndStringProperties: Properties which are value and string types.
* ValueAndStringDicts: Dictionary properties containing value and string types.
* ValueAndStringIEnumerables: IEnumerable properties containing value and string types.
* ClassProperties: Properties which are classes.
* ClassDicts: Dictionary properties containing classes.
* ClassIEnumerables: IEnumerable properties containing classes.

### Installing

* .Net Framework: Install-Package KC.BaseDb
* .Net Core: dotnet add package KC.BaseDb

## Getting Started

```C#
using KC.BaseDb;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;

namespace Example.BaseDb
{
    class Program
    {

        static void Main(string[] args)
        {
            //BaseDb will first try to connect using environment variables, then, if that fails, it will look for a file
            //containing a connection string. If that fails, then it will create said file with a default connection string in it, which
            //you or your users can then edit. You can optionally provide a default value for this connection string.
            //This allows you to easily use a default file for dev connections or persistent deployments,
            //but use environment variables in containerized deployments.

            //NOTE: You must run Add-Migration Init in Nuget (or the dotnet ef equivalent) to create your initial database state.

            AppDbContext.Migrate().Wait();
            AppDbContext.WithContext(async (db) => {
                db.Detectives.AddRange(new Detective[] {
                    new Detective{
                        Name = "Sherlock Holmes",
                        Comment = "At least 100 years ago"
                    },
                    new Detective{
                        Name = "Professor Layton",
                        Comment = "Puzzles!"
                    },
                    new Detective{
                        Name = "Detective Pikachu",
                        Comment = "Is this... for real? I think it is..."
                    },
                });
                await db.SaveChangesAsync();
            }).Wait();
        }
    }

    public class AppDbContext : BaseDbContext<AppDbContext>{
        private static string dbConnectionFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ExampleApp", "db.txt");
        private static string dbConnectionEnvironmentVariable = "Data Source =$|-DBHOST-$|; Initial Catalog = $|-DBNAME-$|; Integrated Security = False; User ID = $|-DBUSER-$|; Password = $|-DBPASS-$|; MultipleActiveResultSets = True";
        private static string hardCodedConnection = $"Data Source =.\\ESR; Initial Catalog = QAppExample; Integrated Security = False; User ID = euler; Password = 3.14159265358979323846264338327; MultipleActiveResultSets = True";

        public AppDbContext() : base("Example", BaseDbType.SqlServer, dbConnectionEnvironmentVariable, dbConnectionFilePath, hardCodedConnection, customDbSetup) {
        }

        public DbSet<Detective> Detectives { get; set; }

        private static void customDbSetup(DbContextOptionsBuilder<AppDbContext> builder) {
            //If you want to use some other type of db:
            //builder.UseSomethingElse();
        }
    }

    public class Detective : BaseModel {
        public string Name { get; set; }
        public string Comment { get; set; }
    }
}

```