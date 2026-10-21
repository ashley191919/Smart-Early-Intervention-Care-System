using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using MySqlConnector;

// Closed-world ADO doubles: there is no socket-capable connection in this harness.
// EF/Pomelo still translates queries/updates; interceptors must handle every command.
sealed class OfflineConnection(List<string> events) : DbConnection
{
    private ConnectionState state;
    [AllowNull] public override string ConnectionString { get; set; } = "";
    public override string Database => "offline_parent";
    public override string DataSource => "NO_NETWORK";
    public override string ServerVersion => "8.0.46";
    public override ConnectionState State => state;
    public bool FailBegin, FailCommit;
    public override void Open() { state = ConnectionState.Open; events.Add("open"); }
    public override Task OpenAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); Open(); return Task.CompletedTask; }
    public override void Close() { state = ConnectionState.Closed; events.Add("close"); }
    public override Task CloseAsync() { Close(); return Task.CompletedTask; }
    public override void ChangeDatabase(string databaseName) => throw new InvalidOperationException("Forbidden");
    public bool Savepoint(string sql)
    {
        if (!sql.StartsWith("SAVEPOINT", StringComparison.Ordinal) && !sql.StartsWith("RELEASE SAVEPOINT", StringComparison.Ordinal) && !sql.StartsWith("ROLLBACK TO SAVEPOINT", StringComparison.Ordinal)) return false;
        events.Add("savepoint"); return true;
    }
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    { events.Add("begin"); if (FailBegin) throw new IOException("Synthetic begin failure"); return new OfflineTransaction(this, isolationLevel, events); }
    protected override DbCommand CreateDbCommand() => new OfflineCommand(this);
}
sealed class OfflineTransaction(OfflineConnection connection, IsolationLevel level, List<string> events) : DbTransaction
{
    private bool complete;
    public override IsolationLevel IsolationLevel => level;
    protected override DbConnection DbConnection => connection;
    public override void Commit() { events.Add("commit"); if (connection.FailCommit) throw new IOException("Synthetic commit failure"); complete = true; }
    public override void Rollback() { events.Add("rollback"); complete = true; }
    protected override void Dispose(bool disposing) { if (disposing) { if (!complete) Rollback(); events.Add("dispose"); } base.Dispose(disposing); }
}
sealed class OfflineCommand(DbConnection connection) : DbCommand
{
    private readonly Parameters parameters = new();
    [AllowNull] public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; } = connection;
    protected override DbParameterCollection DbParameterCollection => parameters;
    protected override DbTransaction? DbTransaction { get; set; }
    public override void Cancel() { }
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new MySqlParameter();
    public override int ExecuteNonQuery() => DbConnection is OfflineConnection c && c.Savepoint(CommandText) ? 0 : throw Unhandled();
    public override object ExecuteScalar() => throw Unhandled();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw Unhandled();
    private static Exception Unhandled() => new InvalidOperationException("Command escaped offline interceptor");
}

sealed class AffectedReader(DbDataReader inner, int affected) : DbDataReader
{
    public override int RecordsAffected => affected;
    public override int Depth => inner.Depth;
    public override int FieldCount => inner.FieldCount;
    public override bool HasRows => inner.HasRows;
    public override bool IsClosed => inner.IsClosed;
    public override object this[int ordinal] => inner[ordinal];
    public override object this[string name] => inner[name];
    public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
    public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
    public override long GetBytes(int ordinal, long offset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, offset, buffer, bufferOffset, length);
    public override char GetChar(int ordinal) => inner.GetChar(ordinal);
    public override long GetChars(int ordinal, long offset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, offset, buffer, bufferOffset, length);
    public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
    public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
    public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
    public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
    public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
    public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
    public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
    public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
    public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
    public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
    public override string GetName(int ordinal) => inner.GetName(ordinal);
    public override int GetOrdinal(string name) => inner.GetOrdinal(name);
    public override string GetString(int ordinal) => inner.GetString(ordinal);
    public override object GetValue(int ordinal) => inner.GetValue(ordinal);
    public override int GetValues(object[] values) => inner.GetValues(values);
    public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
    public override bool NextResult() => inner.NextResult();
    public override bool Read() => inner.Read();
    public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
    public override void Close() => inner.Close();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
sealed class Parameters : DbParameterCollection
{
    private readonly List<DbParameter> values = [];
    public override int Count => values.Count;
    public override object SyncRoot => ((ICollection)values).SyncRoot;
    public override int Add(object value) { values.Add((DbParameter)value); return values.Count - 1; }
    public override void AddRange(Array array) { foreach (var value in array) Add(value!); }
    public override void Clear() => values.Clear();
    public override bool Contains(object value) => values.Contains((DbParameter)value);
    public override bool Contains(string value) => IndexOf(value) >= 0;
    public override void CopyTo(Array array, int index) => ((ICollection)values).CopyTo(array, index);
    public override IEnumerator GetEnumerator() => values.GetEnumerator();
    public override int IndexOf(object value) => values.IndexOf((DbParameter)value);
    public override int IndexOf(string name) => values.FindIndex(p => p.ParameterName == name);
    public override void Insert(int index, object value) => values.Insert(index, (DbParameter)value);
    public override void Remove(object value) => values.Remove((DbParameter)value);
    public override void RemoveAt(int index) => values.RemoveAt(index);
    public override void RemoveAt(string name) => RemoveAt(IndexOf(name));
    protected override DbParameter GetParameter(int index) => values[index];
    protected override DbParameter GetParameter(string name) => values[IndexOf(name)];
    protected override void SetParameter(int index, DbParameter value) => values[index] = value;
    protected override void SetParameter(string name, DbParameter value) { var i = IndexOf(name); if (i < 0) values.Add(value); else values[i] = value; }
}
