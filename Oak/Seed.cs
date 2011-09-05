﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Reflection;
using System.Text.RegularExpressions;
using Oak.Extensions;
using System.Diagnostics;

namespace Oak
{
    [DebuggerNonUserCode]
    public class Seed
    {
        public virtual ConnectionProfile ConnectionProfile { get; set; }

        public Seed()
            : this(null)
        {
        }

        public Seed(ConnectionProfile connectionProfile)
        {
            if (connectionProfile == null) connectionProfile = new ConnectionProfile();
            ConnectionProfile = connectionProfile;
        }

        /// <summary>
        /// Generates script for creating a table.  Use ExecuteNonQuery() 
        /// to execute script.  ExecuteNonQuery is an 
        /// extension method on string (if you dont see the extension method, 
        /// make sure you have using Oak; in your cs file.
        /// </summary>
        /// <returns>Returns the sql command generated by seed.</returns>
        public string CreateTable(string table, dynamic[] columns)
        {
            string columnString = "";

            var primaryKeyColumn = null as string;

            foreach (var entry in columns)
            {
                object column = entry;

                primaryKeyColumn = column.PrimaryKeyColumn();

                columnString += ColumnStringFor(column) + ",";
            }

            return CreateTableCommand(table, columnString, primaryKeyColumn);
        }

        /// <summary>
        /// Generates script to create columns.  Use ExecuteNonQuery()
        /// to execute script.
        /// </summary>
        /// <param name="table"></param>
        /// <param name="columns"></param>
        /// <returns></returns>
        public string AddColumns(string table, dynamic[] columns)
        {
            string columnString = "";

            for (int i = 0; i < columns.Length; i++)
            {
                object column = columns[i];

                columnString += ColumnStringFor(column);

                if (i != columns.Length - 1) columnString += ", ";
            }

            return "ALTER TABLE [dbo].[{0}] ADD {1}"
                .With(table, columnString);
        }

        string ColumnStringFor(object column)
        {
            var name = column.Name();
            var type = column.SqlType();
            var defaultValue = column.DefaultValue();
            var isIdentity = column.IsIdentityColumn();
            var isPrimaryKey = column.IsPrimaryKey();

            string identityAsString = isIdentity ? " IDENTITY(1,1)" : "";

            return "[{0}] {1} {2} {3}{4}"
                        .With(name,
                            type,
                            column.NullDefinition(),
                            column.DefaultValueDefinition(),
                            identityAsString)
                        .ReplaceSequentialSpacesWithSingleSpace()
                        .Trim();
        }

        string CreateTableCommand(string table, string columns, string primaryKeyColumn)
        {
            var primaryKeyScript =
                " CONSTRAINT [PK_{0}] PRIMARY KEY CLUSTERED ([{1}] ASC)".With(table, primaryKeyColumn ?? string.Empty);

            if (primaryKeyColumn == null) primaryKeyScript = "";

            return "CREATE TABLE [dbo].[{0}]({1}{2})".With(table, columns, primaryKeyScript);
        }

        public void PurgeDb()
        {
            DropAllForeignKeys();
            DropAllPrimaryKeys();
            DropAllTables();
        }

        void DropAllForeignKeys()
        {
            var reader = "select name as constraint_name, object_name(parent_obj) as table_name from sysobjects where xtype = 'f'".ExecuteReader(ConnectionProfile);

            while (reader.Read())
            {
                "alter table {0} drop constraint {1} ".With(reader["table_name"], reader["constraint_name"]).ExecuteNonQuery(ConnectionProfile);
            }
        }

        void DropAllPrimaryKeys()
        {
            var reader = "select name as constraint_name, object_name(parent_obj) as table_name from sysobjects where xtype = 'pk'".ExecuteReader(ConnectionProfile);

            while (reader.Read())
            {
                "alter table {0} drop constraint {1} ".With(reader["table_name"], reader["constraint_name"]).ExecuteNonQuery(ConnectionProfile);
            }
        }

        void DropAllTables()
        {
            var reader = "select name as table_name from sysobjects where xtype = 'u'".ExecuteReader(ConnectionProfile);

            while (reader.Read())
            {
                "drop table {0} ".With(reader["table_name"]).ExecuteNonQuery(ConnectionProfile);
            }
        }
    }
}

namespace Oak.Extensions
{
    public static class StringExtensions
    {
        public static string With(this string s, params object[] args)
        {
            return string.Format(s, args);
        }

        public static string ReplaceSequentialSpacesWithSingleSpace(this string s)
        {
            var single = Regex.Replace(s, @"[ ]{2,}", " ");

            return single;
        }
    }

    public static class ColumnExtensions
    {
        public static bool AllowsNulls(this object columnDefinition)
        {
            return !columnDefinition.Properties().Has("Nullable", withValue: false, @in: columnDefinition);
        }

        public static bool IsIdentityColumn(this object columnDefinition)
        {
            return columnDefinition.Properties().Has("Identity", withValue: true, @in: columnDefinition);
        }

        public static bool IsPrimaryKey(this object columnDefinition)
        {
            return columnDefinition.Properties().Has("PrimaryKey", withValue: true, @in: columnDefinition);
        }

        public static object DefaultValue(this object columnDefinition)
        {
            return columnDefinition.Properties().Get("Default", @in: columnDefinition);
        }

        public static object DefaultValueDefinition(this object columnDefinition)
        {
            var defaultValue = columnDefinition.DefaultValue();

            string defaultAsString = "";

            if (defaultValue != null) defaultAsString = "DEFAULT('{0}')".With(defaultValue.ToString());
            else return "";

            var reservedStrings = new[] { "getdate()", "newid()" };

            if (reservedStrings.Contains(defaultValue.ToString().ToLower())) defaultAsString = "DEFAULT({0})".With(defaultValue.ToString());

            return defaultAsString;
        }

        public static string NullDefinition(this object columnDefinition)
        {
            var nullDefinition = columnDefinition.AllowsNulls() ? "NULL" : "NOT NULL";

            if (columnDefinition.IsPrimaryKey() || columnDefinition.IsIdentityColumn()) nullDefinition = "NOT NULL";

            return nullDefinition;
        }

        public static string PrimaryKeyColumn(this object columnDefinition)
        {
            if (columnDefinition.IsPrimaryKey())
            {
                return columnDefinition.Name();
            }

            return null;
        }

        public static string Name(this object columnDefinition)
        {
            return columnDefinition.Properties().First().Name;
        }

        public static bool Has(this PropertyInfo[] properties, string name, bool withValue, object @in)
        {
            return properties.Any(s => s.Name == name && Convert.ToBoolean(s.GetValue(@in, null)) == withValue);
        }

        public static object Get(this PropertyInfo[] properties, string name, object @in)
        {
            var property = properties.SingleOrDefault(s => s.Name == name);

            if (property == null) return null;

            return property.GetValue(@in, null);
        }

        public static string Value(this PropertyInfo propertyInfo, object o)
        {
            return propertyInfo.GetValue(o, null).ToString();
        }

        public static string SqlType(this object columnDefinition)
        {
            return columnDefinition.Properties().First().Value(columnDefinition);
        }

        private static PropertyInfo[] Properties(this object columnDefinition)
        {
            return columnDefinition.GetType().GetProperties();
        }
    }
}