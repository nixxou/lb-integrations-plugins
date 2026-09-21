// A reader that serves someone else's rows, then ours.
//
// WHY NOT A DataTable. The obvious way to add rows to a query result is to load it into a DataTable,
// append, and hand back table.CreateDataReader(). It is what ExtendDB does for its image queries,
// and it carries two costs we do not have to pay here:
//
//   * DataTable.Load asks the reader for its schema, and SqliteDataReader.GetSchemaTable answers by
//     running ANOTHER query on the same command - measured, the stack goes GetSchemaTable ->
//     ExecuteScalar -> ExecuteReader. Anything patched on that path re-enters.
//   * DataTableReader does not narrow Int64 to Int32 the way SqliteDataReader does, so EF throws
//     InvalidCastException on a column SQLite typed as an integer unless every such column is
//     pre-narrowed - and narrowing then breaks any caller that wanted the long.
//
// Chaining avoids both. The host's rows are still served by the host's own reader, one at a time,
// with its own type behaviour untouched; only the rows we appended go through the coercion below.
// Nothing is buffered, nothing is re-queried, and a query we get wrong degrades to "the rows
// LaunchBox would have had anyway".
//
// THE SCHEMA IS THE INNER READER'S. Our rows come from a mirror running the very same SQL, so the
// columns line up by construction; the constructor still refuses a row of the wrong width rather
// than trusting that.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;

namespace LbIntegrations.Flycast
{
    internal sealed class LbipAppendingReader : DbDataReader
    {
        private readonly DbDataReader _inner;
        private readonly IReadOnlyList<object[]> _extra;
        private readonly int _fieldCount;

        private bool _innerDone;
        private int _index = -1;        // position in _extra once the inner reader is exhausted

        public LbipAppendingReader(DbDataReader inner, IReadOnlyList<object[]> extra)
        {
            _inner = inner;
            _extra = extra;
            _fieldCount = inner.FieldCount;
            foreach (var row in extra)
                if (row.Length != _fieldCount)
                    throw new ArgumentException("appended row has " + row.Length + " value(s) for "
                                                + _fieldCount + " column(s)");
        }

        private bool OnExtra => _innerDone && _index >= 0 && _index < _extra.Count;
        private object Raw(int i) => _extra[_index][i];

        public override bool Read()
        {
            if (!_innerDone)
            {
                if (_inner.Read()) return true;
                _innerDone = true;
            }
            return ++_index < _extra.Count;
        }

        // -- shape -----------------------------------------------------------
        public override int FieldCount => _fieldCount;
        public override string GetName(int i) => _inner.GetName(i);
        public override int GetOrdinal(string name) => _inner.GetOrdinal(name);
        public override Type GetFieldType(int i) => _inner.GetFieldType(i);
        public override string GetDataTypeName(int i) => _inner.GetDataTypeName(i);
        public override int Depth => _inner.Depth;
        public override bool IsClosed => _inner.IsClosed;
        public override int RecordsAffected => _inner.RecordsAffected;

        /// <summary>True as soon as WE have rows, even when the host's query found none - which is
        /// exactly the case that matters: an emulator LaunchBox has never heard of.</summary>
        public override bool HasRows => _inner.HasRows || _extra.Count > 0;

        /// <summary>One result set. A second one would not have our columns, and appending to it
        /// would be meaningless.</summary>
        public override bool NextResult() { _innerDone = true; _index = _extra.Count; return false; }

        // -- values ----------------------------------------------------------
        //
        // On the inner rows every call goes straight through, so SqliteDataReader's own conversions
        // apply unchanged. On ours we convert, which is what SqliteDataReader does too: it will hand
        // GetInt32 an integer column stored as a 64-bit value.
        public override object this[int i] => GetValue(i);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override object GetValue(int i) => OnExtra ? (Raw(i) ?? DBNull.Value) : _inner.GetValue(i);
        public override bool IsDBNull(int i) => OnExtra ? (Raw(i) == null || Raw(i) is DBNull) : _inner.IsDBNull(i);

        public override int GetValues(object[] values)
        {
            if (!OnExtra) return _inner.GetValues(values);
            var n = Math.Min(values.Length, _fieldCount);
            for (var i = 0; i < n; i++) values[i] = Raw(i) ?? DBNull.Value;
            return n;
        }

        public override bool GetBoolean(int i) => OnExtra ? Convert.ToBoolean(Raw(i)) : _inner.GetBoolean(i);
        public override byte GetByte(int i) => OnExtra ? Convert.ToByte(Raw(i)) : _inner.GetByte(i);
        public override char GetChar(int i) => OnExtra ? Convert.ToChar(Raw(i)) : _inner.GetChar(i);
        public override short GetInt16(int i) => OnExtra ? Convert.ToInt16(Raw(i)) : _inner.GetInt16(i);
        public override int GetInt32(int i) => OnExtra ? Convert.ToInt32(Raw(i)) : _inner.GetInt32(i);
        public override long GetInt64(int i) => OnExtra ? Convert.ToInt64(Raw(i)) : _inner.GetInt64(i);
        public override float GetFloat(int i) => OnExtra ? Convert.ToSingle(Raw(i)) : _inner.GetFloat(i);
        public override double GetDouble(int i) => OnExtra ? Convert.ToDouble(Raw(i)) : _inner.GetDouble(i);
        public override decimal GetDecimal(int i) => OnExtra ? Convert.ToDecimal(Raw(i)) : _inner.GetDecimal(i);
        public override string GetString(int i) => OnExtra ? Convert.ToString(Raw(i)) : _inner.GetString(i);
        public override DateTime GetDateTime(int i) => OnExtra ? Convert.ToDateTime(Raw(i)) : _inner.GetDateTime(i);
        public override Guid GetGuid(int i)
            => OnExtra ? (Raw(i) is Guid g ? g : Guid.Parse(Convert.ToString(Raw(i)))) : _inner.GetGuid(i);

        public override long GetBytes(int i, long offset, byte[] buffer, int bufferOffset, int length)
        {
            if (!OnExtra) return _inner.GetBytes(i, offset, buffer, bufferOffset, length);
            var source = Raw(i) as byte[] ?? Array.Empty<byte>();
            if (buffer == null) return source.Length;
            var n = (int)Math.Min(length, source.Length - offset);
            if (n <= 0) return 0;
            Array.Copy(source, offset, buffer, bufferOffset, n);
            return n;
        }

        public override long GetChars(int i, long offset, char[] buffer, int bufferOffset, int length)
        {
            if (!OnExtra) return _inner.GetChars(i, offset, buffer, bufferOffset, length);
            var source = Convert.ToString(Raw(i)) ?? "";
            if (buffer == null) return source.Length;
            var n = (int)Math.Min(length, source.Length - offset);
            if (n <= 0) return 0;
            source.CopyTo((int)offset, buffer, bufferOffset, n);
            return n;
        }

        public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

        public override void Close() => _inner.Close();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); }
    }
}
