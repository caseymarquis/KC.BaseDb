﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KC.BaseDb {
    public class DbConnectionSettings {
        private string storedConnectionString = null;
        private object lockEverything = new object();

        public string ConnectionString {
            get {
                lock (lockEverything) {
                    return storedConnectionString;
                }
            }
            set {
                lock (lockEverything) {
                    storedConnectionString = value;
                }
            }
        }

        public void Reset() {
            lock (lockEverything) {
                storedConnectionString = null;
            }
        }

        private BaseDbType m_BaseDbType;
        public BaseDbType BaseDbType {
            get{
                lock (lockEverything) {
                    return m_BaseDbType; 
                }
            }
            set {
                lock (lockEverything) {
                    m_BaseDbType = value;
                }
            }
        }

        private string getConnectionStringFromFile(string filePath) {
            return File.ReadAllLines(filePath).FirstOrDefault(x => x.Trim() != "" && !x.Trim().StartsWith("#"));
        }

        /// <summary>
        /// If the environment variable(s) are found, then the environment variables will be used. If not, the connectionString file will be
        /// looked for. The file will be created if it is not found.
        /// 
        /// Connection string environment variable templates may be used.
        /// They look as follows: DbUser=$|-DBUSER-$|; DbPassword=$|-DBPASS-$|;
        /// </summary>
        /// <returns></returns>
        public string UpdateConnectionString(string appName, BaseDbType baseDbType, string connectionStringEnvironmentVariable, string connectionStringFilePath, string hardCodedConnectionString) {
            lock (lockEverything) {
                var existing = this.storedConnectionString;
                if (existing != null) {
                    return existing;
                }
                m_BaseDbType = baseDbType;
                var cs = "";
                var evTemplate = connectionStringEnvironmentVariable;
                var success = true;
                if (evTemplate == null) {
                    success = false;
                }
                else if (evTemplate.Contains("$|")) {
                    var split = evTemplate.Split(new string[] { "$|" }, StringSplitOptions.None);
                    foreach (var section in split) {
                        if (section.StartsWith("-") && section.EndsWith("-") && section.Length > 2) {
                            var ev = Environment.GetEnvironmentVariable(section.Trim('-'));
                            if (ev == null) {
                                success = false;
                                cs = "";
                                break;
                            }
                            cs += ev;
                        }
                        else {
                            cs += section;
                        }
                    }
                }
                else {
                    cs = Environment.GetEnvironmentVariable(evTemplate);
                    if (cs == null) {
                        cs = "";
                        success = false;
                    }
                    else if (m_BaseDbType == BaseDbType.Postgres && cs.Contains("://")) {
                        var uri = new Uri(cs);
                        var userPass = uri.UserInfo.Split(':');
                        //If the environment variable was in the style of heroku's user:pass@server:port/database, the interpret it:
                        cs = $"Server={uri.Host};Port={uri.Port};Database={uri.LocalPath.Trim('/')};Userid={userPass[0]};Password={userPass[1]};SSL=true;SslMode=Require;";
                    }
                }

                if (success == false) {
                    new FileInfo(connectionStringFilePath).Directory.Create();
                    if (!File.Exists(connectionStringFilePath)) {
                        switch (baseDbType) {
                            case BaseDbType.Postgres:
                                cs = hardCodedConnectionString ?? $"User ID=euler;Password=3.14159265358979323846264338327;Host=localhost;Port=5432;Database={appName};Pooling = true; Min Pool Size = 0; Max Pool Size = 100; Connection Lifetime = 0;";
                                break;
                            case BaseDbType.Sqlite:
                                cs = hardCodedConnectionString ?? "Filename=" + Path.Combine(new FileInfo(connectionStringFilePath).DirectoryName, "db.sqlite");
                                break;
                            case BaseDbType.SqlServer:
                                cs = hardCodedConnectionString ?? $"Data Source =.\\ESR; Initial Catalog = {appName}; Integrated Security = False; User ID = euler; Password = 3.14159265358979323846264338327; MultipleActiveResultSets = True";
                                break;
                            case BaseDbType.Other:
                                cs = hardCodedConnectionString ?? "Put a connection string in this file as the first line!";
                                break;
                        }
                        File.WriteAllText(connectionStringFilePath, cs);
                    }
                    else {
                        cs = getConnectionStringFromFile(connectionStringFilePath);
                    }
                }

                storedConnectionString = cs;
                return cs;
            }
        }

    }
}
