using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.IO;
//using System.Text;
//using System.Threading.Tasks;
using TShockAPI;
using TShockAPI.DB;
using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;

namespace TShockIRC
{
    public class DB
    {
        private IDbConnection db;
        private String table;
        private List<string> fields;
        private Dictionary<Tuple<int, string>, string> data;

        private string paramlist0;
        private string paramlist1;

        public bool Connected
        {
            get
            {
                return (db != null);
            }
        }

        public DB()
        {
            TShockAPI.Hooks.AccountHooks.AccountDelete += OnAccountDelete;
        }

        public DB( String Table, String[] Fields )
        {
            TShockAPI.Hooks.AccountHooks.AccountDelete += OnAccountDelete;
            Connect(Table, Fields);
        }

        void OnAccountDelete(TShockAPI.Hooks.AccountDeleteEventArgs arg)
        {
            if (!Connected)
                return;

            DelUserData(arg.User.ID);
        }

        public void Connect(string Table, String[] Fields)
        {
            Connect(Table, Fields.ToList());
        }

        public void Connect( string Table, List<string> Fields)
        {
            table = Table;
            fields = Fields;

            switch (TShock.Config.StorageType.ToLower())
            {
                case "mysql":
                    string[] dbHost = TShock.Config.MySqlHost.Split(':');
                    db = new MySqlConnection()
                    {
                        ConnectionString = string.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4};",
                            dbHost[0],
                            dbHost.Length == 1 ? "3306" : dbHost[1],
                            TShock.Config.MySqlDbName,
                            TShock.Config.MySqlUsername,
                            TShock.Config.MySqlPassword)
                    };
                    break;

                case "sqlite":
                    string sql = Path.Combine(TShock.SavePath, Table + ".sqlite");
                    db = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
                    break;
            }

            SqlTableCreator sqlcreator = new SqlTableCreator(db, db.GetSqlType() == SqlType.Sqlite ? (IQueryBuilder)new SqliteQueryCreator() : new MysqlQueryCreator());
            
            List<SqlColumn> columns = new List<SqlColumn>();

            columns.Add(new SqlColumn("UserID", MySqlDbType.Int32) { Primary = true, Unique = true, Length = 4 });
            int i = 0;
            foreach (string field in fields)
            {
                columns.Add(new SqlColumn(field, MySqlDbType.Text, 100));
                paramlist0 += ",@" + i.ToString();
                paramlist1 += ",@" + (i + 1).ToString();
                i++;
            }
            paramlist0 = paramlist0.Substring(1);
            paramlist1 = paramlist1.Substring(1);

            sqlcreator.EnsureTableStructure(new SqlTable(table, columns));

            data = new Dictionary<Tuple<int, string>, string> { };
            QueryResult result = db.QueryReader("SELECT * FROM " + table + ";");
            int uid = -1;

            while( result.Read() )
            {
                uid = result.Reader.Get<int>("UserID");
                foreach (string field in fields)
                {
                    data[ new Tuple<int,string>(uid,field) ] = result.Reader.Get<string>(field);
                }
            }

        }

        public string GetUserData(int userid, string field, string defaultval = null)
        {
            if (!fields.Contains(field))
                throw new System.ArgumentException("Field not found.", "field");
                //return null;

            Tuple<int, string> key = new Tuple<int, string>(userid, field);

            if ( data.ContainsKey( key ) )
            {
                return data[key];
            }

            if (!Connected)
                throw new SystemException("Database not connected in GetUserData()");
                //return null;

            // Really, this query shouldn't be necessary because we should always have it in memory. Reading should only happen on object initialization.
            QueryResult result = db.QueryReader("SELECT " + field + " FROM " + table + " WHERE UserID=" + userid + ";");
            
            if (result.Read())
            {
                data[key] = result.Get<String>(field);
                return data[key];
            }
            else
            {
                return defaultval;
            }            
        }

       private void WriteUserData(int userid)
        {
            if (!Connected)
                throw new SystemException("Database not connected in WriteUserData()");

            List<string> vals = new List<string> { userid.ToString() };
            Tuple<int, string> key;
            foreach ( string field in fields )
            {
                key = new Tuple<int, string>(userid, field);
                if (data.ContainsKey(key))
                    vals.Add(data[new Tuple<int, string>(userid, field)]);
                else
                    vals.Add("");
            }
            db.Query("DELETE FROM " + table + " WHERE UserID=@0;", userid);
            db.Query("INSERT INTO " + table + " VALUES (@0," + paramlist1 + ");", vals.ToArray() );
        }

        public void SetUserData(int userid, List<string>values )
        {
            int i = 0;
            foreach ( string value in values )
            {
                data[new Tuple<int, string>(userid, fields[i])] = value;
                i += 1;
            }
            WriteUserData(userid);
            /*
            string all = userid + ",'" + String.Join("','", fields) + "'";
            db.Query("DELETE FROM " + table + " WHERE UserID=@0;", userid);
            db.Query("INSERT INTO " + table + " VALUES (" + all + ");");
            */
        }

        public void SetUserData(int userid, String[] values)
        {
            int i = 0;
            foreach (string value in values)
            {
                data[new Tuple<int, string>(userid, fields[i])] = value;
                i += 1;
            }
            WriteUserData(userid);
            /*
            string all = userid + ",'" + String.Join("','", fields) + "'";
            db.Query("DELETE FROM " + table + " WHERE UserID=@0;", userid);
            db.Query("INSERT INTO " + table + " VALUES (" + all + ");");
            */
        }

        public void SetUserData(int userid, string field, string value)
        {
            if (!fields.Contains(field))
                return;

            data[new Tuple<int, string>(userid, field)] = value;

            if (!Connected)
                return;

            WriteUserData(userid);

            /*
            TShock.Players[0].SendInfoMessage("Checking presense for user " + userid + ".");
            QueryResult result = db.QueryReader("SELECT * FROM " + table + " WHERE UserID=@0;", userid);
            if( ! result.Read() )
            {
                TShock.Players[0].SendInfoMessage("No data found for user " + userid + ", creating.");
                db.Query("INSERT INTO " + table + " (UserID, " + field + ") VALUES (@0, @1);", userid, "");
            }

            TShock.Players[0].SendInfoMessage("Sending update query.");
            db.Query("UPDATE " + table + " SET " + field + "='" + data + "' WHERE UserID=" + userid + ";");

            TShock.Players[0].SendInfoMessage("Update query sent.");

            //db.Query("UPDATE " + table + " SET " + field + "=@1 WHERE Key=@0; IF @@ROWCOUNT = 0 INSERT INTO " + table + " (" + field + ") VALUES (@1);", userid, data);
            //db.Query("INSERT INTO " + table + "(UserId, " + field + ") VALUES (@0, @1);", userid, data);
            */
        }

        public void ResetAllUserData(int userid, List<string> values )
        {
            if (!Connected)
                throw new SystemException("Database not connected in ResetAllUserData()");

            int uid = -1;
            QueryResult allids = db.QueryReader("SELECT UserID FROM " + table + ";");
            while( allids.Read() )
            {
                uid = allids.Get<int>("UserID");
                SetUserData(uid, values);
            }
        }

        public void ResetAllUserData(int userid, string field, string value )
        {
            if (!Connected)
                throw new SystemException("Database not connected in ResetAllUserData()");

            int uid = -1;
            QueryResult allids = db.QueryReader("SELECT UserID FROM " + table + ";");
            while (allids.Read())
            {
                uid = allids.Get<int>("UserID");
                SetUserData(uid, field, value);
            }
        }

        public void DelUserData(int userid)
        {
            foreach( string field in fields )
            {
                if( data.ContainsKey( new Tuple<int,string>(userid,field) ))
                {
                    data.Remove(new Tuple<int, string>(userid, field));
                }
            }
            db.Query("DELETE FROM " + table + " WHERE UserID=@0;", userid);
        }

        private void clearDB()
        {
            data = new Dictionary<Tuple<int, string>, string> { };
            db.Query("DELETE FROM " + table);
        }
    }
}
