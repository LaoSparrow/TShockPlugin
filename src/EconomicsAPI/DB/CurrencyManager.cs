﻿using EconomicsAPI.Enumerates;
using EconomicsAPI.EventArgs.PlayerEventArgs;
using EconomicsAPI.Events;
using MySql.Data.MySqlClient;
using System.Data;
using TShockAPI;
using TShockAPI.DB;

namespace EconomicsAPI.DB;

public class CurrencyManager
{
    private readonly Dictionary<string, long> Currency = new();

    public IDbConnection database;

    internal CurrencyManager()
    {
        this.database = TShock.DB;
        var Skeleton = new SqlTable("Economics",
            new SqlColumn("ID", MySqlDbType.Int32) { Length = 8, Primary = true, Unique = true, AutoIncrement = true },
            new SqlColumn("UserName", MySqlDbType.Text) { Length = 500 },
            new SqlColumn("Currency", MySqlDbType.Int64) { Length = 255 });
        var List = new SqlTableCreator(this.database, this.database.GetSqlType() == SqlType.Sqlite ? new SqliteQueryCreator() : new MysqlQueryCreator());
        List.EnsureTableStructure(Skeleton);
        using (var reader = this.database.QueryReader("SELECT * FROM Economics"))
        {
            while (reader.Read())
            {
                var username = reader.Get<string>("UserName");
                var currency = reader.Get<long>("Currency");
                this.Currency[username] = currency;
            }
        }
    }

    public void UpdataAll()
    {
        foreach (var name in this.Currency.Keys)
        {
            if (!this.Update(name))
            {
                this.Write(name);
            }
        }
    }

    public bool Update(string UserName)
    {
        return this.database.Query("UPDATE `economics` SET `Currency` = @0 WHERE `economics`.`UserName` = @1", this.GetUserCurrency(UserName), UserName) == 1;
    }
    public bool Write(string UserName)
    {
        return this.database.Query("INSERT INTO `economics` (`ID`, `UserName`, `Currency`) VALUES (NULL, @0, @1)", UserName, this.GetUserCurrency(UserName)) == 1;
    }

    public long GetUserCurrency(string name)
    {
        return this.Currency.TryGetValue(name, out var currency) ? currency : 0;
    }

    public void AddUserCurrency(string name, long amount)
    {
        var player = Economics.ServerPlayers.Find(x => x.Name == name && x.Active);
        if (player != null)
        {
            var args = new PlayerCurrencyUpdateArgs()
            {
                Change = amount,
                CurrentType = CurrencyUpdateType.Added,
                Player = player,
                Current = this.Currency.TryGetValue(name, out var cur) ? cur : 0
            };
            if (PlayerHandler.PlayerCurrencyUpdate(args))
            {
                return;
            }

            amount = args.Change;
        }
        this.Add(name, amount);
    }

    private void Add(string name, long amount)
    {
        if (this.Currency.ContainsKey(name))
        {
            this.Currency[name] += amount;
        }
        else
        {
            this.Currency[name] = amount;
        }
    }

    public void ClearUserCurrency(string name)
    {
        this.Currency[name] = 0;
    }

    private bool Deduct(string name, long amount)
    {
        if (amount == 0)
        {
            return true;
        }

        if (this.GetUserCurrency(name) >= amount)
        {
            this.Currency[name] -= amount;
            return true;
        }
        return false;
    }

    [Obsolete("use DeductUserCurrency instead")]
    public bool DelUserCurrency(string name, long amount)
    {
        return this.DeductUserCurrency(name, amount);
    }

    public bool DeductUserCurrency(string name, long amount)
    {
        var player = Economics.ServerPlayers.Find(x => x.Name == name && x.Active);
        if (player != null)
        {
            var args = new PlayerCurrencyUpdateArgs()
            {
                Change = amount,
                CurrentType = CurrencyUpdateType.Delete,
                Player = player,
                Current = this.Currency.TryGetValue(name, out var cur) ? cur : 0
            };
            if (PlayerHandler.PlayerCurrencyUpdate(args))
            {
                return true;
            }

            amount = args.Change;
        }
        return this.Deduct(name, amount);
    }

    public void Reset()
    {
        this.database.Query("delete from Economics");
        this.Currency.Clear();
    }
}