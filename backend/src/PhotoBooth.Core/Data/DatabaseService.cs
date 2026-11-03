using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoBooth.Core.Data;

public class DatabaseService : IDisposable
{
    private readonly string _connectionString;
    private readonly string _dbPath;
    private SQLiteConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DatabaseService(string? dbPath = null)
    {
        if (string.IsNullOrEmpty(dbPath))
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string hihiDir = Path.Combine(appData, "HihiBooth", "Data");
            if (!Directory.Exists(hihiDir)) Directory.CreateDirectory(hihiDir);
            _dbPath = Path.Combine(hihiDir, "store.db");
        }
        else
        {
            _dbPath = dbPath;
            string? dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        _connectionString = $"Data Source={_dbPath};Version=3;Journal Mode=WAL;BusyTimeout=5000;";
    }

    public async Task InitializeAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _connection = new SQLiteConnection(_connectionString);
            await _connection.OpenAsync();

            string initSql = @"
                CREATE TABLE IF NOT EXISTS Transactions (
                    Id TEXT PRIMARY KEY,
                    SessionId TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    Amount INTEGER DEFAULT 0,
                    PaymentMethod TEXT DEFAULT 'Free',
                    Status TEXT DEFAULT 'Completed',
                    Copies INTEGER DEFAULT 1,
                    LayoutType TEXT DEFAULT 'strip2x6',
                    PaperUsed INTEGER DEFAULT 1
                );

                CREATE TABLE IF NOT EXISTS Fileitems (
                    Id TEXT PRIMARY KEY,
                    TransactionId TEXT NOT NULL,
                    FilePath TEXT NOT NULL,
                    FileType TEXT NOT NULL,
                    FileSize INTEGER DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (TransactionId) REFERENCES Transactions(Id)
                );

                CREATE INDEX IF NOT EXISTS idx_transactions_created ON Transactions(CreatedAt);
                CREATE INDEX IF NOT EXISTS idx_fileitems_tx ON Fileitems(TransactionId);
            ";

            using var cmd = new SQLiteCommand(initSql, _connection);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RecordTransactionAsync(
        string txId,
        string sessionId,
        string layoutType,
        int copies,
        string paymentMethod = "Free",
        int amount = 0)
    {
        await _lock.WaitAsync();
        try
        {
            if (_connection == null || _connection.State != ConnectionState.Open)
            {
                _connection = new SQLiteConnection(_connectionString);
                await _connection.OpenAsync();
            }

            string sql = @"
                INSERT OR REPLACE INTO Transactions 
                (Id, SessionId, CreatedAt, Amount, PaymentMethod, Status, Copies, LayoutType, PaperUsed)
                VALUES (@id, @sessionId, @createdAt, @amount, @paymentMethod, 'Completed', @copies, @layoutType, @paperUsed);
            ";

            using var cmd = new SQLiteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@id", txId);
            cmd.Parameters.AddWithValue("@sessionId", sessionId);
            cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@amount", amount);
            cmd.Parameters.AddWithValue("@paymentMethod", paymentMethod);
            cmd.Parameters.AddWithValue("@copies", copies);
            cmd.Parameters.AddWithValue("@layoutType", layoutType);
            cmd.Parameters.AddWithValue("@paperUsed", copies);

            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddFileItemAsync(string fileId, string txId, string filePath, string fileType)
    {
        await _lock.WaitAsync();
        try
        {
            if (_connection == null || _connection.State != ConnectionState.Open)
            {
                _connection = new SQLiteConnection(_connectionString);
                await _connection.OpenAsync();
            }

            long size = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;

            string sql = @"
                INSERT OR REPLACE INTO Fileitems (Id, TransactionId, FilePath, FileType, FileSize, CreatedAt)
                VALUES (@id, @txId, @path, @type, @size, @createdAt);
            ";

            using var cmd = new SQLiteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@id", fileId);
            cmd.Parameters.AddWithValue("@txId", txId);
            cmd.Parameters.AddWithValue("@path", filePath);
            cmd.Parameters.AddWithValue("@type", fileType);
            cmd.Parameters.AddWithValue("@size", size);
            cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o"));

            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<Dictionary<string, object>>> GetRecentTransactionsAsync(int limit = 10)
    {
        await _lock.WaitAsync();
        try
        {
            if (_connection == null || _connection.State != ConnectionState.Open)
            {
                _connection = new SQLiteConnection(_connectionString);
                await _connection.OpenAsync();
            }

            string sql = $"SELECT * FROM Transactions ORDER BY CreatedAt DESC LIMIT {limit};";
            using var cmd = new SQLiteCommand(sql, _connection);
            using var reader = await cmd.ExecuteReaderAsync();

            var list = new List<Dictionary<string, object>>();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.GetValue(i);
                }
                list.Add(row);
            }
            return list;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
        _lock.Dispose();
    }
}
